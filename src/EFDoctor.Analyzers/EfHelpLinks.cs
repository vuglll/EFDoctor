namespace EFDoctor.Analyzers;

public static class EfHelpLinks
{
    // The main branch holds releases only, so a rule page there matches the published package.
    public const string RulePagesUrl = "https://github.com/vuglll/EFDoctor/blob/main/docs/rules/";

    public static string For(string diagnosticId) => RulePagesUrl + diagnosticId + ".md";
}
