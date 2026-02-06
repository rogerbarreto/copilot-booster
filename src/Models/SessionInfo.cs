namespace CopilotApp.Models;

class SessionInfo
{
    public string Id { get; set; } = "";
    public string Cwd { get; set; } = "";
    public string Summary { get; set; } = "";
    public int Pid { get; set; }
}
