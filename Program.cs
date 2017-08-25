﻿using System;
using System.IO;
using System.Linq;
using System.Xml;
using System.Reflection;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Shell.NET;
using ConsoleColors;

class Program
{
    static Assembly tool = Assembly.GetExecutingAssembly();
    static string  toolName = tool.GetName().Name;
    static string toolVersion = FileVersionInfo.GetVersionInfo(tool.Location).ProductVersion;
    static string home = Environment.GetEnvironmentVariable("HOME");
    static string configDir = $"{home}/.netpkg-tool";
    static int width = 64;
    static Bash bash = new Bash();
    static string csproj;
    static string projectDir;
    static string destination;
    static string DllName;
    static string AppName;
    static string dotNetVersion;
    static string Here = AppDomain.CurrentDomain.BaseDirectory;
    static string[] Args;

    static bool Verbose = false;
    static bool SkipRestore = false;
    static bool CustomAppName = false;
    static bool SelfContainedDeployment = false;
    static bool KeepTempFiles = false;
    

    static void Main(string[] args)
    {
        SayHello();
        ParseArgs(args);
        CheckPaths(args);
        FindCsproj(args[0]);
        SayTask(projectDir, $"{destination}/{AppName}");
        if (!SkipRestore) RestoreProject();
        ComileProject();
        TransferFiles();
        RunAppImageTool();
        if (!KeepTempFiles) DeleteTempFiles();
        SayFinished($"New AppImage created at {destination}/{AppName}");
        SayBye();
    }

    static void CheckPaths(string[] args)
    {
        if (args.Length < 2 || !Directory.Exists(args[0]) && !Directory.Exists(args[1]))
            ExitWithError("You must specify a valid .NET project AND destination folder.\n", 1);
        if (Directory.Exists(args[0]) && !Directory.Exists(args[1]))
            ExitWithError($"{args[1]} is not a valid folder\n", 2);
        if (!Directory.Exists(args[0]) && Directory.Exists(args[1]))
            ExitWithError($"{args[0]} is not a valid folder\n", 3);
        
        projectDir = GetRelativePath(args[0]);
        destination = GetRelativePath(args[1]);
    }

    static void ParseArgs(string[] args)
    {
        Args = args;

        if (args == null || args.Length == 0)
            HelpMenu();
        
        if (args[0] == "--clear-log")
            ClearLogs();

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "-v" || args[i] == "--verbose")
            {
                Verbose = true;
            }
            else if (args[i] == "-c" || args[i] == "--compile")
            {
                SkipRestore = true;
            }
            else if (args[i] == "-n" || args[i] == "--name")
            {
                CustomAppName = true;
                AppName = args[i + 1];
            }
            else if (args[i] == "-s" || args[i] == "--scd")
            {
                SelfContainedDeployment = true;
            }
            else if (args[i] == "-k" || args[i] == "--keep")
            {
                KeepTempFiles = true;
            }
            else if (args[i] == "-h" || args[i] == "--help")
            {
                HelpMenu();
            }
        }
    }

    static void FindCsproj(string project)
    {
        var location = bash.Command($"find {project} -maxdepth 1 -name '*.csproj'", redirect: true).Lines;

        if (location.Length < 1)
            ExitWithError($"No .csproj found in {GetRelativePath(project)}\n", 10);
        if (location.Length > 1)
            ExitWithError($"More than one .csproj found in {GetRelativePath(project)}\n", 11);
        
        var folderSplit = location[0].Split('/');
        csproj = folderSplit[folderSplit.Length - 1];
        projectDir = GetRelativePath(project);
        dotNetVersion = GetCoreVersion();
        var nameSplit = csproj.Split('.');
        DllName = string.Join('.', nameSplit.Take(nameSplit.Length - 1));

        if (!CustomAppName)
            AppName = DllName;
    }

    static string GetCoreVersion()
    {
        var path = GetAbsolutePath($"{projectDir}/{csproj}");
        var node = "/Project/PropertyGroup/TargetFramework";
        var xml = new XmlDocument();
        xml.LoadXml(File.ReadAllText(path));
        return xml.DocumentElement.SelectSingleNode(node).InnerText;
    }

    static void RestoreProject()
    {
        if (Verbose)
        {
            Console.WriteLine("Restoring .NET project dependencies...");
            bash.Command($"cd {projectDir} && dotnet restore", redirect: false);
        }
        else
        {
            Console.Write("Restoring .NET project dependencies...");
            bash.Command($"cd {projectDir} && dotnet restore", redirect: true);
        }
        
        CheckCommandOutput(errorCode: 20);
    }

    static void ComileProject()
    {
        string cmd;

        if (SelfContainedDeployment)
            cmd = $"cd {projectDir} && dotnet publish -c Release -r linux-x64 --no-restore";
        else 
            cmd = $"cd {projectDir} && dotnet publish -c Release --no-restore";

        if (Verbose)
        {
            Console.WriteLine("Compiling .NET project...");
            bash.Command(cmd, redirect: false);
        }
        else
        {
            Console.Write("Compiling .NET project...");
            bash.Command(cmd, redirect: true);
        }
        
        CheckCommandOutput(errorCode: 21);
    }

    static void TransferFiles()
    {
        var path = $"{Here}/file-transfer.sh";
        string cmd;

        if (SelfContainedDeployment)
            cmd = $"{path} {projectDir} {DllName} {AppName} {dotNetVersion} {toolVersion} true";
        else
            cmd = $"{path} {projectDir} {DllName} {AppName} {dotNetVersion} {toolVersion}";
        
        Console.Write("Transferring files...");
        bash.Command(cmd, redirect: true);
        CheckCommandOutput(errorCode: 22);
    }

    static void RunAppImageTool()
    {
        var appimgtool = $"{Here}/appimagetool/AppRun";
        var cmd = $"{appimgtool} -n /tmp/{AppName}.temp {destination}/{AppName}";

        if (Verbose)
        {
            Console.WriteLine("Compressing with appimagetool...");
            bash.Command(cmd, redirect: false);
        }
        else
        {
            Console.Write("Compressing with appimagetool...");
            bash.Command(cmd, redirect: true);
        }
        
        CheckCommandOutput(errorCode: 23);
    }
    
    static void DeleteTempFiles()
    {
        Console.Write("Deleting temporary files...");
        bash.Rm($"/tmp/{DllName}.temp", "-rf");
        CheckCommandOutput(24);
    }

    static void SayHello()
    {
        var title = $" {toolName} v{toolVersion} ";
        var newWidth = width - title.Length;
        var leftBar = new String('-', newWidth / 2);
        string rightBar;

        if (newWidth % 2 > 0)
            rightBar = new String('-', newWidth / 2 + 1);
        else
            rightBar = new String('-', newWidth / 2);
        
        Printer.WriteLine($"\n{leftBar}{Clr.Cyan}{Frmt.Bold}{title}{Reset.Code}{rightBar}");
    }

    static void HelpMenu()
    {
        Printer.WriteLine(
            $"\n            {Frmt.Bold}{Clr.Cyan}Usage:{Reset.Code}\n"
            + $"    {Frmt.Bold}netpkg-tool{Frmt.UnBold} "
            + $"[{Frmt.Underline}Project{Reset.Code}] "
            + $"[{Frmt.Underline}Destination{Reset.Code}] "
            + $"[{Frmt.Underline}Flags{Reset.Code}]\n\n"
            + $"            {Frmt.Bold}{Clr.Cyan}Flags:{Reset.Code}\n"
            + $"     --verbose or -v: Verbose output\n"
            + $"     --compile or -c: Skip restoring dependencies\n"
            + $"        --name or -n: Set ouput file to a custom name\n"
            + $"         --scd or -s: Self-Contained Deployment (SCD)\n"
            + @"        --keep or -k: Keep /tmp/{AppName}.temp directory\n"
            + $"        --help or -h: Help menu (this page)\n\n"
            + $"    More information & source code available on github:\n"
            + $"    https://github.com/phil-harmoniq/netpkg-tool\n"
            + $"    Copyright (c) 2017 - MIT License\n"
        );
        SayBye();
        Environment.Exit(0);
    }

    static void ClearLogs()
    {
        Console.Write($"Clear log at {GetRelativePath(configDir)}/error.log");
        bash.Rm($"{configDir}/error.log", "-f");
        CheckCommandOutput(errorCode: 5);
        SayBye();
        Environment.Exit(0);
    }

    static void ExitWithError(string message, int code)
    {
        if (Verbose)
        {
            WriteToErrorLog("[Error message was written to verbose output]", code);
        }
        else
        {
            Printer.Write($"{Clr.Red}{message}{Clr.Default}");
            WriteToErrorLog(message, code);
        }
        SayBye();
        Environment.Exit(code);
    }

    static void WriteToErrorLog(string message, int code)
    {
        if (!Directory.Exists(configDir))
            Directory.CreateDirectory(configDir);
        
        using (var tw = new StreamWriter($"{configDir}/error.log", true))
        {
            var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
            var dir = Directory.GetCurrentDirectory();

            tw.WriteLine($"{new string('-', width)}");
            tw.WriteLine($"{GetRelativePath(dir)}$ netpkg-tool {string.Join(' ', Args)}");
            tw.WriteLine($"Errored with code {code} - ({now}):\n");
            tw.WriteLine(message.TrimEnd('\n'));
            tw.WriteLine($"{new string('-', width)}");
        }
    }

    /// <param name="errorCode">Desired error code if the command didn't run properly</param>
    static void CheckCommandOutput(int errorCode = 1)
    {
        if (bash.ExitCode != 0)
        {
            SayFail();
            if (string.IsNullOrEmpty(bash.ErrorMsg))
                ExitWithError(bash.Output, errorCode);
            else
                ExitWithError(bash.ErrorMsg, errorCode);
        }
        SayPass();
    }
    
    static string GetRelativePath(string path) =>
        bash.Command($"cd {path} && dirs -0", redirect: true).Lines[0];

    static string GetAbsolutePath(string path) =>
        bash.Command($"readlink -f {path}", redirect: true).Lines[0];
    
    static void SayBye() =>
        Console.WriteLine(new String('-', width) + "\n");

    static void SayTask(string project, string destination) =>
        Printer.WriteLine($"{Clr.Cyan}{project} => {destination}{Clr.Default}");

    static void SayFinished(string message) =>
        Printer.WriteLine($"{Clr.Green}{message}{Clr.Default}");

    static void SayPass() =>
        Printer.WriteLine($" {Frmt.Bold}[ {Clr.Green}PASS{Clr.Default} ]{Reset.Code}");

    static void SayFail() =>
        Printer.WriteLine($" {Frmt.Bold}[ {Clr.Red}FAIL{Clr.Default} ]{Reset.Code}");
}
