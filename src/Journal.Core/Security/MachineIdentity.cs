namespace Journal.Core.Security;

/// <summary>Abstraction of the PC identity so tests can simulate a second machine.</summary>
public interface IMachineIdentity
{
    string MachineName { get; }
}

public sealed class EnvironmentMachineIdentity : IMachineIdentity
{
    public string MachineName => Environment.MachineName;
}
