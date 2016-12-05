namespace TraceService
{
    public interface IHealthReporter
    {
        void ReportHealthy();

        void ReportProblem(string problemDescription);
    }
}