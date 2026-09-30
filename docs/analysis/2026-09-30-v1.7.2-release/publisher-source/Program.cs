using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using Steamworks;

// One release only. Inspect performs no Workshop mutations; publish updates the existing item only.
class Program
{
    const string Content = "G:/SteamLibrary/steamapps/common/RimWorld/Mods/MapGenAI";
    const string ExpectedDll = "9a792fad321743582ee767548082b0ae72d23643d4f8e26358a42bee5b1052b5";
    const ulong Item = 3685385453;
    const uint App = 294100;
    static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
    static readonly Dictionary<string, object> Receipt = new Dictionary<string, object>();
    static string receiptPath;
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool SetDllDirectory(string path);

    static string Hash(byte[] data)
    {
        using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "").ToLowerInvariant();
    }
    static string HashFile(string path) => Hash(File.ReadAllBytes(path));
    static void Save() => File.WriteAllText(receiptPath, Json.Serialize(Receipt), new UTF8Encoding(false));
    static void Wait(Func<bool> done, int seconds)
    {
        var timer = Stopwatch.StartNew();
        while (!done())
        {
            SteamAPI.RunCallbacks();
            if (timer.Elapsed.TotalSeconds > seconds) throw new TimeoutException("Steam operation outcome is unknown; check server before retrying.");
            Thread.Sleep(100);
        }
    }
    static void CheckContent(string installationReceipt)
    {
        var installation = Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(installationReceipt));
        var expected = (Dictionary<string, object>)installation["installed"];
        var files = Directory.GetFiles(Content, "*", SearchOption.AllDirectories);
        if (files.Length != expected.Count) throw new Exception("Installed file list changed.");
        foreach (var path in files)
        {
            string rel = path.Substring(Content.Length + 1).Replace('\\', '/');
            if (!expected.ContainsKey(rel) || HashFile(path) != (string)expected[rel]) throw new Exception("Installed file changed: " + rel);
        }
        if (HashFile(Path.Combine(Content, "Assemblies/MapGenAI.dll")) != ExpectedDll) throw new Exception("Wrong release DLL.");
        if (File.ReadAllText(Path.Combine(Content, "About/PublishedFileId.txt")).Trim() != Item.ToString()) throw new Exception("Wrong Workshop item.");
        Receipt["contentFileCount"] = files.Length;
        Receipt["contentFiles"] = expected;
        Receipt["dllSha256"] = ExpectedDll;
    }
    static Dictionary<string, object> Details(SteamUGCDetails_t d) => new Dictionary<string, object> {
        ["publishedFileId"] = d.m_nPublishedFileId.m_PublishedFileId.ToString(),
        ["title"] = d.m_rgchTitle, ["consumerAppId"] = d.m_nConsumerAppID.m_AppId,
        ["creatorAppId"] = d.m_nCreatorAppID.m_AppId, ["timeUpdated"] = d.m_rtimeUpdated,
        ["fileSize"] = d.m_nFileSize, ["previewSize"] = d.m_nPreviewFileSize,
        ["descriptionSha256"] = Hash(Encoding.UTF8.GetBytes(d.m_rgchDescription ?? "")),
        ["tags"] = d.m_rgchTags, ["visibility"] = d.m_eVisibility.ToString(),
        ["ownerMatchesLoggedInUser"] = d.m_ulSteamIDOwner == SteamUser.GetSteamID().m_SteamID
    };
    static SteamUGCDetails_t Query()
    {
        var handle = SteamUGC.CreateQueryUGCDetailsRequest(new[] { new PublishedFileId_t(Item) }, 1);
        if (!SteamUGC.SetReturnLongDescription(handle, true)) throw new Exception("Cannot request full description.");
        SteamUGCDetails_t details = default;
        bool done = false, ok = false;
        var callback = CallResult<SteamUGCQueryCompleted_t>.Create((result, failure) => {
            ok = !failure && result.m_eResult == EResult.k_EResultOK && result.m_unNumResultsReturned == 1;
            if (ok) ok = SteamUGC.GetQueryUGCResult(handle, 0, out details) && details.m_eResult == EResult.k_EResultOK;
            done = true;
        });
        try
        {
            callback.Set(SteamUGC.SendQueryUGCRequest(handle));
            Wait(() => done, 30);
            if (!ok) throw new Exception("Workshop detail query failed.");
            return details;
        }
        finally { callback.Dispose(); SteamUGC.ReleaseQueryUGCRequest(handle); }
    }
    static int Main(string[] args)
    {
        bool initialized = false;
        if (args.Length < 3 || !new[] { "inspect", "publish", "download" }.Contains(args[0])) return 2;
        receiptPath = Path.GetFullPath(args[1]);
        Receipt["mode"] = args[0]; Receipt["startedAt"] = DateTimeOffset.Now.ToString("o");
        Receipt["publishedFileId"] = Item.ToString(); Receipt["submissionStarted"] = false;
        try
        {
            CheckContent(args[2]);
            Environment.SetEnvironmentVariable("SteamAppId", App.ToString());
            Environment.SetEnvironmentVariable("SteamGameId", App.ToString());
            if (!SetDllDirectory("G:/SteamLibrary/steamapps/common/RimWorld/RimWorldWin64_Data/Plugins/x86_64")) throw new Exception("Native DLL directory rejected.");
            initialized = SteamAPI.Init();
            if (!initialized) throw new Exception("Steam API initialization failed; no upload submitted.");
            if (SteamUtils.GetAppID().m_AppId != App || !SteamUser.BLoggedOn()) throw new Exception("Wrong app or Steam user is offline.");
            var before = Query(); Receipt["before"] = Details(before); Save();
            if (before.m_nPublishedFileId.m_PublishedFileId != Item || before.m_nConsumerAppID.m_AppId != App || before.m_ulSteamIDOwner != SteamUser.GetSteamID().m_SteamID)
                throw new Exception("Existing Workshop item/app/owner does not match.");
            if (args[0] == "inspect") { Receipt["completed"] = true; Save(); Console.WriteLine("Preflight OK: existing item, owner, app and installed release match."); return 0; }
            if (args[0] == "download")
            {
                if ((SteamUGC.GetItemState(new PublishedFileId_t(Item)) & 1U) == 0) throw new Exception("Item is not subscribed; subscription not changed.");
                bool accepted = SteamUGC.DownloadItem(new PublishedFileId_t(Item), true);
                Receipt["downloadAccepted"] = accepted; Receipt["completed"] = accepted; Save();
                Console.WriteLine("Existing subscription download accepted: " + accepted); return accepted ? 0 : 1;
            }
            if (args.Length != 4) throw new Exception("Publish requires an explicit reviewed change-note file.");
            string notes = File.ReadAllText(args[3]); Receipt["changeNote"] = notes;
            CheckContent(args[2]);
            var update = SteamUGC.StartItemUpdate(new AppId_t(App), new PublishedFileId_t(Item));
            if (!SteamUGC.SetItemContent(update, Path.GetFullPath(Content))) throw new Exception("Content rejected; no submit.");
            bool submitted = false;
            using (var callback = CallResult<SubmitItemUpdateResult_t>.Create((result, failure) => {
                Receipt["result"] = result.m_eResult.ToString(); Receipt["ioFailure"] = failure;
                Receipt["returnedItem"] = result.m_nPublishedFileId.m_PublishedFileId.ToString();
                Receipt["legalAgreementRequired"] = result.m_bUserNeedsToAcceptWorkshopLegalAgreement;
                Receipt["success"] = !failure && result.m_eResult == EResult.k_EResultOK && result.m_nPublishedFileId.m_PublishedFileId == Item && !result.m_bUserNeedsToAcceptWorkshopLegalAgreement;
                submitted = true; Save();
            }))
            {
                Receipt["submissionStarted"] = true; Save();
                callback.Set(SteamUGC.SubmitItemUpdate(update, notes));
                Console.WriteLine("Submitted existing Workshop item " + Item + ".");
                Wait(() => submitted, 240);
            }
            Receipt["after"] = Details(Query()); Receipt["finishedAt"] = DateTimeOffset.Now.ToString("o"); Save();
            Console.WriteLine("Upload result: " + Receipt["result"]);
            return (bool)Receipt["success"] ? 0 : 1;
        }
        catch (Exception error)
        {
            Receipt["error"] = error.ToString(); Save(); Console.Error.WriteLine(error.Message); return 1;
        }
        finally { if (initialized) SteamAPI.Shutdown(); }
    }
}
