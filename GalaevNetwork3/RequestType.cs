namespace GalaevNetwork3
{
    public enum RequestType : byte
    {
        Connect = 1,
        Disconnect = 2,
        Start = 4,
        Data = 8,
        End = 16
    }
}