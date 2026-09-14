namespace MapGenAI.LLM
{
    public static class InvalidEditExplanation
    {
        public static string Instruction(string reason) =>
            "The proposed edit failed the same validation used to apply it. Nothing was changed. Reason: " + reason +
            "\nReturn ONLY {\"action\":\"ask\",\"message\":\"...\"}. Explain the conflict in the user's language using feature labels. " +
            "Do not return generate or silently remove/replace an existing feature to pass validation. " +
            "Offer one concrete compatible alternative that preserves existing features, or ask which feature the user wants to replace. " +
            "Do not offer multiple alternatives in one yes/no question. A vague yes to multiple alternatives does not choose one or authorize both. " +
            "Use the current catalog and state. Do not expose JSON keys as instructions to the user.";
    }
}
