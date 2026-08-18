using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.ServiceProcess;
using System.Xml;

class SyncthingService : ServiceBase
{
    private const string SvcName = "Syncthing";
    private static readonly string BaseDir = AppDomain.CurrentDomain.BaseDirectory;
    private static readonly string SyncthingExe = Path.Combine(BaseDir, "syncthing.exe");
    private static readonly string LogPath = Path.Combine(BaseDir, "syncthing.log");
    private static readonly string ArgsFile = Path.Combine(BaseDir, "syncthing-args.txt");
    private static readonly string HomeFile = Path.Combine(BaseDir, "syncthing-home.txt");
    private static readonly string ConfigFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Syncthing", "config.xml");

    private readonly object logLock = new object();
    private Process proc;
    private System.Threading.Timer watchdog;

    static void Main(string[] args)
    {
        if (args.Length == 0)
        {
            ServiceBase.Run(new SyncthingService());
            return;
        }
        switch (args[0].ToLower())
        {
            case "install":
                Install();
                break;
            case "uninstall":
                Sc("stop " + SvcName);
                Sc("delete " + SvcName);
                break;
            case "start":
                StartService(args);
                break;
            case "params":
                SaveParams(args);
                break;
            case "home":
                SetHome(args);
                break;
            case "stop":
                Sc("stop " + SvcName);
                break;
            case "status":
                Sc("query " + SvcName);
                break;
            case "run":
                new SyncthingService().RunConsole();
                break;
            default:
                Console.WriteLine("Usage:");
                Console.WriteLine("  SyncthingService.exe install          - install as a Windows service");
                Console.WriteLine("  SyncthingService.exe uninstall        - remove the service");
                Console.WriteLine("  SyncthingService.exe start [params...] - start the service, extra params are");
                Console.WriteLine("                                          passed to syncthing.exe for this run");
                Console.WriteLine("  SyncthingService.exe params [args...]  - save permanent extra args for syncthing.exe");
                Console.WriteLine("                                          (no args = clear); stored in syncthing-args.txt");
                Console.WriteLine("  SyncthingService.exe home [path]        - show/set Syncthing config dir (--home)");
                Console.WriteLine("                                          stored in syncthing-home.txt; set automatically");
                Console.WriteLine("                                          during install (service runs as LocalSystem)");
                Console.WriteLine("  SyncthingService.exe stop              - stop the service");
                Console.WriteLine("  SyncthingService.exe status            - show service state");
                Console.WriteLine("  SyncthingService.exe run               - run in console (testing, no service)");
                break;
        }
    }

    static void Install()
    {
        string exe = Process.GetCurrentProcess().MainModule.FileName;
        string bin = "\"" + "\"" + exe + "\"" + "\"";
        Sc("create " + SvcName + " binPath= " + bin + " start= auto DisplayName= \"Syncthing (service wrapper)\"");
        Sc("description " + SvcName + " \"Syncthing run as a Windows service\"");
        Sc("failure " + SvcName + " reset= 86400 actions= restart/5000/restart/10000/restart/30000");
        if (!File.Exists(HomeFile))
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string candidate = Path.Combine(localAppData, "Syncthing");
            if (File.Exists(Path.Combine(candidate, "config.xml")))
            {
                File.WriteAllText(HomeFile, candidate);
                Console.WriteLine("Config dir saved to " + HomeFile + ": " + candidate);
            }
            else
            {
                Console.WriteLine("Note: config.xml not found in " + candidate + ". Set it later with: " + exe + " home <path>");
            }
        }
        Console.WriteLine("Service '" + SvcName + "' installed.");
        Console.WriteLine("Start it with: " + exe + " start");
    }

    static void StartService(string[] args)
    {
        string cmd = "start " + SvcName;
        for (int i = 1; i < args.Length; i++)
            cmd += " \"" + args[i] + "\"";
        Sc(cmd);
    }

    static void SaveParams(string[] args)
    {
        string line = "";
        for (int i = 1; i < args.Length; i++)
            line += (line.Length > 0 ? " " : "") + (args[i].Contains(" ") ? "\"" + args[i] + "\"" : args[i]);
        if (line.Length == 0)
        {
            if (File.Exists(ArgsFile)) File.Delete(ArgsFile);
            Console.WriteLine("Permanent args cleared.");
        }
        else
        {
            File.WriteAllText(ArgsFile, line);
            Console.WriteLine("Permanent args saved to " + ArgsFile + ": " + line);
        }
    }

    static void SetHome(string[] args)
    {
        if (args.Length > 1)
        {
            string path = args[1];
            if (path.Equals("clear", StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(HomeFile)) File.Delete(HomeFile);
                Console.WriteLine("Config dir cleared (syncthing will use its default).");
            }
            else
            {
                File.WriteAllText(HomeFile, path);
                Console.WriteLine("Config dir saved to " + HomeFile + ": " + path);
            }
            return;
        }
        if (File.Exists(HomeFile))
            Console.WriteLine("Config dir: " + File.ReadAllText(HomeFile).Trim());
        else
            Console.WriteLine("Config dir not set. Set it with: home <path>");
    }

    static void Sc(string command)
    {
        var psi = new ProcessStartInfo("sc.exe", command)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        using (var p = Process.Start(psi))
        {
            Console.Write(p.StandardOutput.ReadToEnd());
            Console.Write(p.StandardError.ReadToEnd());
            p.WaitForExit();
        }
    }

    protected override void OnStart(string[] args)
    {
        try
        {
            if (!File.Exists(SyncthingExe))
                throw new FileNotFoundException("syncthing.exe not found: " + SyncthingExe);
            string extraArgs = ReadSavedArgs();
            string scArgs = string.Join(" ", Array.ConvertAll(args, a => a.Contains(" ") ? "\"" + a + "\"" : a));
            if (scArgs.Length > 0) extraArgs = (extraArgs.Length > 0 ? extraArgs + " " : "") + scArgs;
            if (!extraArgs.Contains("--no-restart"))
                extraArgs = extraArgs.Length > 0 ? "--no-restart " + extraArgs : "--no-restart";
            string home = ReadHomeDir();
            if (home.Length > 0 && !extraArgs.Contains("--home"))
                extraArgs += " --home=\"" + home + "\"";
            Log("=== Syncthing service starting ===");
            Log("Syncthing args: " + extraArgs);
            var psi = new ProcessStartInfo
            {
                FileName = SyncthingExe,
                Arguments = extraArgs,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = BaseDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            psi.EnvironmentVariables["STNOUPGRADE"] = "1";
            proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
            proc.OutputDataReceived += (s, e) => { if (e.Data != null) Log(e.Data); };
            proc.ErrorDataReceived += (s, e) => { if (e.Data != null) Log(e.Data); };
            proc.Exited += (s, e) => Log("Syncthing process exited, code " + proc.ExitCode);
            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            Log("Syncthing started, PID " + proc.Id);
            watchdog = new System.Threading.Timer(_ => WatchdogTick(), null, 30000, 30000);
        }
        catch (Exception ex)
        {
            Log("Failed to start: " + ex.Message);
            throw;
        }
    }

    private static string ReadSavedArgs()
    {
        try
        {
            if (File.Exists(ArgsFile))
                return File.ReadAllText(ArgsFile).Trim();
        }
        catch { }
        return "";
    }

    private static string ReadHomeDir()
    {
        try
        {
            if (File.Exists(HomeFile))
                return File.ReadAllText(HomeFile).Trim();
        }
        catch { }
        return "";
    }

    private void WatchdogTick()
    {
        try
        {
            if (proc != null && proc.HasExited)
            {
                Log("Syncthing is not running anymore, stopping the service");
                Stop();
            }
        }
        catch { }
    }

    protected override void OnStop()
    {
        Log("=== Syncthing service stopping ===");
        if (watchdog != null) watchdog.Dispose();
        if (proc != null && !proc.HasExited)
        {
            StopGracefully();
            if (!proc.WaitForExit(20000))
            {
                Log("Syncthing did not exit within 20s, killing it");
                KillProc();
            }
            else
            {
                Log("Syncthing exited gracefully");
            }
        }
        Log("=== Syncthing service stopped ===");
    }

    private void StopGracefully()
    {
        try
        {
            string key = ReadApiKey();
            if (key == null)
            {
                Log("API key not found, skipping graceful shutdown");
                return;
            }
            var req = (HttpWebRequest)WebRequest.Create(GetGuiUrl());
            req.Method = "POST";
            req.Headers["X-API-Key"] = key;
            req.Timeout = 5000;
            using (var resp = (HttpWebResponse)req.GetResponse()) { }
            Log("Graceful shutdown requested via REST API");
        }
        catch (Exception ex)
        {
            Log("REST shutdown failed: " + ex.Message);
        }
    }

    private static string GetGuiUrl()
    {
        try
        {
            if (File.Exists(ConfigFile))
            {
                var doc = new XmlDocument();
                doc.Load(ConfigFile);
                var gui = doc.SelectSingleNode("/configuration/gui");
                if (gui != null)
                {
                    var addrNode = gui.SelectSingleNode("address");
                    string addr = addrNode != null ? addrNode.InnerText.Trim() : "127.0.0.1:8384";
                    bool tls = gui.Attributes["tls"] != null && gui.Attributes["tls"].Value == "true";
                    if (!addr.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    {
                        if (addr.StartsWith(":")) addr = "127.0.0.1" + addr;
                        addr = (tls ? "https://" : "http://") + addr;
                    }
                    return addr.TrimEnd('/') + "/rest/system/shutdown";
                }
            }
        }
        catch { }
        return "http://127.0.0.1:8384/rest/system/shutdown";
    }

    private static string ReadApiKey()
    {
        try
        {
            if (!File.Exists(ConfigFile)) return null;
            var doc = new XmlDocument();
            doc.Load(ConfigFile);
            var gui = doc.SelectSingleNode("/configuration/gui");
            return gui != null && gui.Attributes["apikey"] != null ? gui.Attributes["apikey"].Value : null;
        }
        catch { return null; }
    }

    private void KillProc()
    {
        try
        {
            var psi = new ProcessStartInfo("taskkill.exe", "/PID " + proc.Id + " /T /F")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using (var k = Process.Start(psi))
                k.WaitForExit(10000);
            Log("Syncthing process terminated");
        }
        catch (Exception ex) { Log("Failed to kill process: " + ex.Message); }
    }

    private void RunConsole()
    {
        Console.WriteLine("Starting Syncthing in console mode... (Ctrl+C to stop)");
        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            OnStop();
            Environment.Exit(0);
        };
        OnStart(null);
        if (proc != null) proc.WaitForExit();
    }

    private void Log(string line)
    {
        if (line == null) return;
        lock (logLock)
        {
            try
            {
                using (var fs = new FileStream(LogPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                using (var w = new StreamWriter(fs))
                    w.WriteLine(line);
            }
            catch { }
        }
    }
}
