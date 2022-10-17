using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using JamUp.Waves.RuntimeScripts.API;
using pbuddy.TypeScriptingUtility.RuntimeScripts;
using Unity.Entities;
using UnityEngine;


public partial class LocalCodeRunnerSystem : SystemBase
{
    private static readonly string BaseDirectory = GetFolderInHome("WavyNthLocal");
    
    private const string JavascriptExecutableFileName = "bundle.js";

    private const string TypescriptAPIOutputFileName = "api.ts";

    private FileSystemWatcher watcher;

    private readonly API api = new ();
    private readonly string codeFile = Path.Combine(BaseDirectory, JavascriptExecutableFileName);

    private bool doRun = true;
    protected override void OnCreate()
    {
        base.OnCreate();

        if (!Directory.Exists(BaseDirectory)) throw new Exception($"{BaseDirectory} folder not found");

        string apiFile = Path.Combine(BaseDirectory, TypescriptAPIOutputFileName);

        File.WriteAllText(apiFile, api.Generate());

        watcher = new FileSystemWatcher(BaseDirectory);

        watcher.Filter = $"{JavascriptExecutableFileName}*";
        
        watcher.NotifyFilter = NotifyFilters.Attributes
                               | NotifyFilters.CreationTime
                               | NotifyFilters.DirectoryName
                               | NotifyFilters.FileName
                               | NotifyFilters.LastAccess
                               | NotifyFilters.LastWrite
                               | NotifyFilters.Security
                               | NotifyFilters.Size;
        
        watcher.Changed += (_, e) =>
        {
            if (e.Name.Contains("map")) return;
            doRun = true;
        };
        
        watcher.EnableRaisingEvents = true;
    }
    
    protected override void OnUpdate()
    {
        if (!doRun) return;
        JsRunner.ExecuteFile(codeFile, context => context.ApplyAPI(api));
        doRun = false;
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        watcher?.Dispose();
    }

    private static string GetFolderInHome(string folderName) =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), folderName);
}