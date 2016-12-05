namespace TraceService
{
    public interface IConfigurationProvider
    {
        bool HasConfiguration { get; }

        string GetValue(string name);
    }
}