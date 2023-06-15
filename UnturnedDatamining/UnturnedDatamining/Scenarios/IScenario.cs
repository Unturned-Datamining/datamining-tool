namespace UnturnedDatamining.Scenarios;
internal interface IScenario
{
    /// <summary>
    /// Starts the scenario
    /// </summary>
    /// <param name="unturnedPath">The root of Unturned path</param>
    /// <param name="args">Optional args to get custom settings</param>
    /// <returns><see langword="true"/> when <see cref="IScenario"/> successfully completed, otherwise <see langword="false"/></returns>
    Task<bool> StartAsync(string unturnedPath, string[] args);

    Task WriteCommitToFileAsync(string path, string fileName);
}
