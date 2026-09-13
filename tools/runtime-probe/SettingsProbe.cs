using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using MapGenAI.UI;
using UnityEngine;
using Verse;

namespace MapGenAI.RuntimeProbe
{
    // Exercises the production settings renderer within an actual Verse window.
    public sealed class SettingsProbe : MonoBehaviour
    {
        string output;
        int stage, firstFrame;
        ProbeWindow window;
        readonly List<string> errors = new List<string>();
        readonly List<string> checks = new List<string>();
        List<string> models;
        Task<List<string>> fetching;
        float fetchStarted;
        static readonly string[] modes = {"simple", "advanced-cloud", "advanced-local", "model-menu"};
        string modelConfigPath;
        bool catalogRequested, menuCaptured;
        float stageStarted;
        public static void Start(string output)
        {
            var obj = new GameObject("MapGenAI Settings Probe");
            var probe = obj.AddComponent<SettingsProbe>();
            probe.output = output;
            Application.logMessageReceived += probe.OnLog;
            probe.firstFrame=Time.frameCount;
            if(GenCommandLine.TryGetCommandLineArg("mapgenAIModelConfig",out var configPath))
            {
                probe.modelConfigPath=configPath;
                var config=SimpleJson.Parse(File.ReadAllText(configPath));
                probe.fetching=(Task<List<string>>)typeof(MapGenAISettings).GetMethod("FetchGeminiModels",BindingFlags.Instance|BindingFlags.NonPublic)
                    .Invoke(new MapGenAISettings(),new object[]{config.GetString("gemini_api_key")});
                probe.fetchStarted=Time.realtimeSinceStartup;
            }
        }
        void OnLog(string message, string stack, LogType type)
        {
            if(message.Contains("GUIClip") || type == LogType.Exception)
                if(errors.Count < 12) errors.Add(modes[Math.Min(stage,modes.Length-1)]+": "+message);
        }
        void Open()
        {
            window = new ProbeWindow(stage);
            Find.WindowStack.Add(window);
            firstFrame = Time.frameCount;
            stageStarted=Time.realtimeSinceStartup;
            if(stage==3)
            {
                var config=SimpleJson.Parse(File.ReadAllText(modelConfigPath));
                // Use the production async queue, but keep credentials out of the visible settings fixture.
                var request=new ApiConfig{ApiKey=config.GetString("gemini_api_key")};
                var target=window.settings;
                typeof(MapGenAISettings).GetMethod("FetchModelsForConfig",BindingFlags.Instance|BindingFlags.NonPublic)
                    .Invoke(target,new object[]{request,(Action<string>)(m=>target.simpleGeminiModel=m),(Func<bool>)(()=>target.geminiApiKey=="")});
                catalogRequested=true;
            }
        }
        void Update()
        {
            int elapsed = Time.frameCount-firstFrame;
            // The startup callback runs before Root_Entry has a WindowStack.
            if(window==null) {if(elapsed>=10)Open();return;}
            if(stage!=3 && elapsed == 30) ScreenCapture.CaptureScreenshot(Path.Combine(output,"settings-"+modes[stage]+".png"));
            if(stage==3)
            {
                typeof(MapGenAISettings).GetMethod("ApplyPendingModels",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(window.settings,null);
                bool hasMenu=Find.WindowStack.WindowOfType<FloatMenu>()!=null;
                if(!hasMenu && !menuCaptured && Time.realtimeSinceStartup-stageStarted<45)return;
                menuCaptured=hasMenu;
            }
            // FloatMenu closes itself when the pointer stays outside; exercise actions promptly.
            if(elapsed < (stage==3?1:60)) return;
            if(stage==2 && fetching!=null)
            {
                if(!fetching.IsCompleted && Time.realtimeSinceStartup-fetchStarted<45)return;
                try
                {
                    if(!fetching.IsCompleted)throw new TimeoutException("Model listing timed out");
                    models=fetching.GetAwaiter().GetResult();
                    File.WriteAllText(Path.Combine(output,"live-gemini-models.json"),SimpleJson.Serialize(models));
                    Require(models.Count>2,"actual Gemini catalog contains more than two models");
                }
                catch {errors.Add("model catalog/menu checks failed");}
                fetching=null;
            }
            if(stage==3)
            {
                try {CheckMenus(window.settings);}
                catch(Exception error)
                {
                    errors.Add("production menu action failed: "+error.GetType().Name+": "+error.Message);
                    var flags=BindingFlags.Instance|BindingFlags.NonPublic;
                    var cache=(Dictionary<string,List<string>>)typeof(MapGenAISettings).GetField("_cachedModels",flags).GetValue(window.settings);
                    var failures=(Dictionary<string,string>)typeof(MapGenAISettings).GetField("_fetchErrors",flags).GetValue(window.settings);
                    File.WriteAllText(Path.Combine(output,"catalog-status.json"),SimpleJson.Serialize(new Dictionary<string,object>{{"cacheCount",cache.Count},{"errors",failures.Values.ToList()}}));
                }
            }
            window.Close(false);
            if(++stage < (models==null?3:4)) {Open();return;}
            Application.logMessageReceived -= OnLog;
            File.WriteAllText(Path.Combine(output,"result.json"),SimpleJson.Serialize(new Dictionary<string,object>{
                {"ok",errors.Count==0},{"renderedModes",modes.Take(models==null?3:4).ToArray()},{"checks",checks},{"guiErrors",errors}
            }));
            enabled=false;
            Application.Quit();
        }
        void Require(bool value,string label)
        {
            if(!value){checks.Add("FAIL: "+label);throw new InvalidOperationException(label);}
            checks.Add("PASS: "+label);
        }
        static List<FloatMenuOption> Options(FloatMenu menu) => (List<FloatMenuOption>)typeof(FloatMenu)
            .GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)
            .First(f=>f.FieldType==typeof(List<FloatMenuOption>)).GetValue(menu);
        void CheckMenus(MapGenAISettings settings)
        {
            var show=typeof(MapGenAISettings).GetMethod("ShowModelFloatMenu",BindingFlags.Static|BindingFlags.NonPublic);
            var menu=Find.WindowStack.WindowOfType<FloatMenu>();
            Require(catalogRequested && menu!=null,"production async fetch completion opens its target menu");
            var options=Options(menu);
            Require(options.Count==models.Distinct().Count()+1,"every fetched model is present in the production menu plus refresh");
            string target=models.First(m=>m!="gemini-3.8-flash" && m!="gemini-2.5-flash");
            options.First(o=>o.Label==target).action();
            Require(settings.GetActiveConfig().SelectedModel==target,"selecting a third model updates the active simple configuration");
            settings.geminiApiKey="changed-account";
            string other=models.First(m=>m!=target);
            options.First(o=>o.Label==other).action();
            Require(settings.GetActiveConfig().SelectedModel==target,"an obsolete menu cannot change model selection");
            menu.Close(false);
            var configured=new ApiConfig{ApiKey="fixture",SelectedModel="existing-advanced-model"};
            settings.cloudConfigs.Clear();
            settings.useSimpleMode=false;settings.useCloudProviders=true;settings.cloudConfigs.Add(configured);
            show.Invoke(null,new object[]{models,(Action<string>)(m=>configured.SelectedModel=m),(Func<bool>)(()=>true),(Action)(()=>{})});
            menu=Find.WindowStack.WindowOfType<FloatMenu>();
            Options(menu).First(o=>o.Label==other).action();
            Require(settings.GetActiveConfig().SelectedModel==other,"advanced selection updates its configured provider");
            menu.Close(false);
            settings.useSimpleMode=true;settings.simpleGeminiModel="user-specified-future-model";
            Require(settings.GetActiveConfig().SelectedModel=="user-specified-future-model","manual model IDs are not replaced by the default");
        }
        sealed class ProbeWindow : Window
        {
            public readonly MapGenAISettings settings = new MapGenAISettings();
            readonly int mode;
            public override Vector2 InitialSize => new Vector2(820,540);
            public ProbeWindow(int mode)
            {
                this.mode=mode;
                settings.useSimpleMode=mode==0 || mode==3;
                settings.useCloudProviders=mode==1;
                for(int i=0;i<12;i++)settings.cloudConfigs.Add(new ApiConfig{
                    Provider=i%2==0?LLMProvider.Gemini:LLMProvider.OpenAI,
                    SelectedModel=i%2==0?"gemini-3.8-flash":"existing-user-model",ApiKey="fixture-only"
                });
                doCloseX=false;draggable=false;forcePause=true;
            }
            public override void DoWindowContents(Rect rect)
            {
                Widgets.Label(new Rect(0,0,rect.width,30),"MapGen AI — "+modes[mode]);
                if(mode!=3)settings.DoWindowContents(new Rect(0,36,rect.width,rect.height-36));
            }
        }
    }
}
