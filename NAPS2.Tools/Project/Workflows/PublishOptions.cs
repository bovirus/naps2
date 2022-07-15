using CommandLine;

namespace NAPS2.Tools.Project.Workflows;

[Verb("publish", HelpText = "Build, test, package, and verify standard targets")]
public class PublishOptions
{
    [Value(0, MetaName = "build type", Required = false, HelpText = "all|exe|msi|zip")]
    public string? BuildType { get; set; }
    
    [Option('p', "platform", Required = false, HelpText = "all|win32|win64|mac|macarm|linux")]
    public string? Platform { get; set; }

    [Option("nocleanup", Required = false, HelpText = "Skip cleaning up temp files")]
    public bool NoCleanup { get; set; }

    [Option('v', "verbose", Required = false, HelpText = "Show full output")]
    public bool Verbose { get; set; }
}