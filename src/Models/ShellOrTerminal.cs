using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace SourceGit.Models
{
    public class ShellOrTerminal
    {
        public string Type { get; set; }
        public string Name { get; set; }
        public string Exec { get; set; }
        public string Args { get; set; }
        public bool IsInternal { get; set; }

        public Bitmap Icon
        {
            get
            {
                var iconType = Type;
                if (IsInternal)
                {
                    if (OperatingSystem.IsWindows())
                    {
                        if (Type == "git-bash")
                            iconType = "git-bash";
                        else if (Type == "pwsh" || Type == "powershell")
                            iconType = "pwsh";
                        else if (Type == "wsl")
                            iconType = "custom";
                        else
                            iconType = "cmd"; // cmd and powershell
                    }
                    else if (OperatingSystem.IsMacOS())
                        iconType = "mac-terminal";
                    else
                        iconType = "custom"; // Linux
                }

                try
                {
                    var icon = AssetLoader.Open(
                        new Uri($"avares://SourceGit/Resources/Images/ShellIcons/{iconType}.png",
                        UriKind.RelativeOrAbsolute));
                    return new Bitmap(icon);
                }
                catch
                {
                    return null;
                }
            }
        }

        public static readonly List<ShellOrTerminal> Supported;

        static ShellOrTerminal()
        {
            Supported = new List<ShellOrTerminal>();

            if (OperatingSystem.IsWindows())
            {
                var gitExe = Native.OS.FindGitExecutable();
                if (!string.IsNullOrEmpty(gitExe))
                {
                    var gitRoot = Path.GetDirectoryName(Path.GetDirectoryName(gitExe));
                    var gitBash = Path.Combine(gitRoot, "bin", "bash.exe");
                    if (!File.Exists(gitBash))
                        gitBash = Path.Combine(gitRoot, "usr", "bin", "bash.exe");

                    if (File.Exists(gitBash))
                        Supported.Add(new ShellOrTerminal("git-bash", "Git Bash", gitBash, "--login -i") { IsInternal = true });
                }

                // AddIfExeExists("cmd", "Command Prompt", "cmd.exe", null, true);
                AddIfExeExists("pwsh", "pwsh 7", "pwsh.exe", "-NoLogo", true);
                AddIfExeExists("wsl", "WSL", "wsl.exe", "", true);
                AddIfExeExists("powershell", "PowerShell", "powershell.exe", "-NoLogo", true);
                AddIfExeExists("wt", "Windows Terminal", "wt.exe", "-d .");
            }
            else if (OperatingSystem.IsMacOS())
            {
                AddIfExeExists("zsh", "Zsh", "/bin/zsh", "-i", true);
                AddIfExeExists("bash", "Bash", "/bin/bash", "-i", true);
                AddIfExeExists("sh", "Sh", "/bin/sh", "-i", true);
                AddIfExeExists("mac-terminal", "Terminal", "Terminal");
                AddIfExeExists("iterm2", "iTerm", "iTerm");
                AddIfExeExists("warp", "Warp", "Warp");
                AddIfExeExists("ghostty", "Ghostty", "Ghostty");
                AddIfExeExists("kitty", "kitty", "kitty");
            }
            else
            {
                AddIfExeExists("bash", "Bash", "/bin/bash", "-i", true);
                AddIfExeExists("bash", "Bash", "/usr/bin/bash", "-i", true);
                AddIfExeExists("zsh", "Zsh", "/bin/zsh", "-i", true);
                AddIfExeExists("zsh", "Zsh", "/usr/bin/zsh", "-i", true);
                AddIfExeExists("fish", "Fish", "/usr/bin/fish", "-i", true);
                AddIfExeExists("sh", "Sh", "/bin/sh", "-i", true);
                AddIfExeExists("gnome-terminal", "Gnome Terminal", "gnome-terminal");
                AddIfExeExists("konsole", "Konsole", "konsole");
                AddIfExeExists("xfce4-terminal", "Xfce4 Terminal", "xfce4-terminal");
                AddIfExeExists("wezterm", "WezTerm", "wezterm", "start --cwd .");
                AddIfExeExists("ghostty", "Ghostty", "ghostty");
                AddIfExeExists("kitty", "kitty", "kitty");
            }

            Supported.Add(new ShellOrTerminal("custom", "Custom", ""));
        }

        private static void AddIfExeExists(string type, string name, string exec, string args = null, bool isInternal = false)
        {
            bool exists = false;
            if (Path.IsPathRooted(exec))
            {
                exists = File.Exists(exec);
            }
            else
            {
                var paths = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator);
                if (paths != null)
                {
                    foreach (var path in paths)
                    {
                        if (File.Exists(Path.Combine(path, exec)))
                        {
                            exists = true;
                            break;
                        }
                    }
                }
            }

            if (exists)
            {
                // Avoid duplicates (e.g. bash in multiple paths)
                if (!Supported.Exists(x => x.Name == name || (x.Exec == exec && x.Args == args)))
                {
                    Supported.Add(new ShellOrTerminal(type, name, exec, args) { IsInternal = isInternal });
                }
            }
        }

        public ShellOrTerminal(string type, string name, string exec, string args = null)
        {
            Type = type;
            Name = name;
            Exec = exec;
            Args = args;
        }
    }
}
