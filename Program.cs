using illinkrepro;
using System.CommandLine;

var rootCommand = new RootCommand()
{
    Create.Command
};

return rootCommand.Invoke(args);