using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using dnlib.DotNet.Writer;
using dnlib.PE;
using dnlib.W32Resources;
using Microsoft.Win32;

namespace PaintDotNet.QuickExport.Patcher
{
    class Program
    {
        private const string CompanionDllName = "PaintDotNet.QuickExport.dll";
        private const string ExporterTypeName = "PaintDotNet.QuickExport.Exporter";

        static int Main(string[] args)
        {
            Console.Title = "Paint.NET Quick Export Patcher";

            // Elevated installer worker
            if (args.Length >= 3 && args[0].Equals("--install", StringComparison.OrdinalIgnoreCase))
            {
                return RunElevatedInstall(args[1], args[2]);
            }

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("==================================================================");
            Console.WriteLine("          Paint.NET Quick Export Patcher                          ");
            Console.WriteLine("==================================================================");
            Console.ResetColor();
            Console.WriteLine();

            // Locate Paint.NET installation directory
            string targetDir = FindPaintDotNetDirectory();
            if (string.IsNullOrEmpty(targetDir))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("Could not automatically locate Paint.NET installation directory.");
                Console.ResetColor();
                Console.Write("Please enter the path to the Paint.NET folder (e.g. C:\\Program Files\\paint.net): ");
                targetDir = Console.ReadLine()?.Trim('\"', ' ');
            }

            if (string.IsNullOrEmpty(targetDir) || !Directory.Exists(targetDir))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Invalid directory. Aborting.");
                Console.ResetColor();
                Pause();
                return 1;
            }

            string dllPath = Path.Combine(targetDir, "paintdotnet.dll");
            string origDllPath = Path.Combine(targetDir, "paintdotnet.orig.dll");

            if (!File.Exists(dllPath) && !File.Exists(origDllPath))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Error: Neither paintdotnet.dll nor paintdotnet.orig.dll was found in:\n{targetDir}");
                Console.ResetColor();
                Pause();
                return 1;
            }

            Console.WriteLine($"Found Paint.NET at: {targetDir}");
            Console.WriteLine();

            // Check if Paint.NET is currently running
            EnsurePaintDotNetClosed();

            // Check state of Paint.NET binaries
            bool activeIsPatched = IsAssemblyPatched(dllPath);
            bool origExists = File.Exists(origDllPath);

            // Paint.NET Update Detection: If orig exists, but active DLL is unpatched, Paint.NET was updated!
            if (origExists && !activeIsPatched)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("Detected that Paint.NET was updated (active paintdotnet.dll is a new vanilla version).");
                Console.WriteLine("Updating vanilla backup to match the new version...");
                Console.ResetColor();
                try
                {
                    File.Copy(dllPath, origDllPath, true);
                }
                catch { }
            }

            // Check if user wants to restore original
            if (origExists && activeIsPatched)
            {
                Console.WriteLine("Options:");
                Console.WriteLine("  [1] Patch / Update Quick Export (Default)");
                Console.WriteLine("  [2] Restore original Paint.NET (Unpatch)");
                Console.Write("Enter choice [1/2]: ");
                string choice = Console.ReadLine()?.Trim();
                if (choice == "2")
                {
                    return RestoreOriginal(targetDir, dllPath, origDllPath);
                }
            }

            // Determine source DLL to patch from
            string sourceDll;
            if (!activeIsPatched)
            {
                // Active DLL is vanilla (fresh install or Paint.NET was updated)
                sourceDll = dllPath;
            }
            else if (origExists)
            {
                // Active DLL is already patched; patch from vanilla backup
                sourceDll = origDllPath;
            }
            else
            {
                sourceDll = dllPath;
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("Warning: paintdotnet.dll is already patched, but paintdotnet.orig.dll was not found.");
                Console.WriteLine("Skipping backup to avoid saving a patched file as original.");
                Console.ResetColor();
            }
            Console.WriteLine($"Loading assembly to patch from: {sourceDll}");

            ModuleDefMD mod;
            byte[] sourceBytes = null;
            bool sourceIsPatched = false;
            try
            {
                sourceBytes = File.ReadAllBytes(sourceDll);
                mod = ModuleDefMD.Load(sourceBytes);
                sourceIsPatched = IsAssemblyPatched(mod);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Failed to load module: {ex.Message}");
                Console.ResetColor();
                Pause();
                return 1;
            }

            try
            {
                ApplyPatches(mod);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Patching failed: {ex.Message}\n{ex.StackTrace}");
                Console.ResetColor();
                Pause();
                return 1;
            }

            // Get companion DLL bytes
            byte[] companionBytes = GetCompanionDllBytes();
            if (companionBytes == null || companionBytes.Length == 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Error: Could not locate '{CompanionDllName}' to deploy with the patch.");
                Console.WriteLine($"Ensure '{CompanionDllName}' is placed in the same folder as this patcher.");
                Console.ResetColor();
                Pause();
                return 1;
            }

            // Deploy patch via staging-first workflow
            DeployPatch(mod, companionBytes, sourceBytes, sourceDll, targetDir, sourceIsPatched);

            Pause();
            return 0;
        }

        private static void ApplyPatches(ModuleDefMD mod)
        {
            if (IsAssemblyPatched(mod))
            {
                Console.WriteLine(" -> Assembly already contains Quick Export hooks. Skipping hook injection.");
                return;
            }

            var fileMenu = mod.Find("PaintDotNet.Menus.FileMenu", true);
            if (fileMenu == null) throw new InvalidOperationException("Could not find PaintDotNet.Menus.FileMenu in assembly.");

            var onOpening = fileMenu.FindMethod("OnDropDownOpening");
            if (onOpening == null) throw new InvalidOperationException("Could not find FileMenu.OnDropDownOpening.");

            var fileOnClicked = fileMenu.FindMethod("OnDropDownItemClicked");
            var pdnMenuItem = mod.Find("PaintDotNet.Menus.PdnMenuItem", true);
            var pdnOnClicked = pdnMenuItem?.FindMethod("OnDropDownItemClicked");
            var pdnMainMenu = mod.Find("PaintDotNet.Menus.PdnMainMenu", true);
            var pdnMainOnClicked = pdnMainMenu?.FindMethod("OnItemClicked");

            // 1. Bypass PdnMainMenu.OnItemClicked
            if (pdnMainOnClicked != null)
            {
                var baseCall = pdnMainOnClicked.Body.Instructions.FirstOrDefault(i => i.OpCode == OpCodes.Call && i.Operand?.ToString().Contains("OnItemClicked") == true)?.Operand as IMethodDefOrRef;
                if (baseCall != null)
                {
                    pdnMainOnClicked.Body.Instructions.Clear();
                    pdnMainOnClicked.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
                    pdnMainOnClicked.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_1));
                    pdnMainOnClicked.Body.Instructions.Add(Instruction.Create(OpCodes.Call, baseCall));
                    pdnMainOnClicked.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
                    pdnMainOnClicked.Body.ExceptionHandlers.Clear();
                    Console.WriteLine(" -> Patched PdnMainMenu.OnItemClicked");
                }
            }

            // 2. Bypass PdnMenuItem.OnDropDownItemClicked
            if (pdnOnClicked != null)
            {
                var baseCall = pdnOnClicked.Body.Instructions.FirstOrDefault(i => i.OpCode == OpCodes.Call && i.Operand?.ToString().Contains("ToolStripDropDownItem::OnDropDownItemClicked") == true)?.Operand as IMethodDefOrRef;
                if (baseCall != null)
                {
                    pdnOnClicked.Body.Instructions.Clear();
                    pdnOnClicked.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
                    pdnOnClicked.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_1));
                    pdnOnClicked.Body.Instructions.Add(Instruction.Create(OpCodes.Call, baseCall));
                    pdnOnClicked.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
                    pdnOnClicked.Body.ExceptionHandlers.Clear();
                    Console.WriteLine(" -> Patched PdnMenuItem.OnDropDownItemClicked");
                }
            }

            // 3. Bypass FileMenu.OnDropDownItemClicked
            if (fileOnClicked != null)
            {
                var baseCall = fileOnClicked.Body.Instructions.FirstOrDefault(i => i.OpCode == OpCodes.Call && i.Operand?.ToString().Contains("OnDropDownItemClicked") == true)?.Operand as IMethodDefOrRef;
                if (baseCall != null)
                {
                    fileOnClicked.Body.Instructions.Clear();
                    fileOnClicked.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
                    fileOnClicked.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_1));
                    fileOnClicked.Body.Instructions.Add(Instruction.Create(OpCodes.Call, baseCall));
                    fileOnClicked.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
                    fileOnClicked.Body.ExceptionHandlers.Clear();
                    Console.WriteLine(" -> Patched FileMenu.OnDropDownItemClicked");
                }
            }

            // 4. Inject Dynamic Reflection Loader into FileMenu.OnDropDownOpening
            var m1 = mod.Import(typeof(AppDomain).GetProperty("CurrentDomain").GetGetMethod());
            var m2 = mod.Import(typeof(AppDomain).GetProperty("BaseDirectory").GetGetMethod());
            var m3 = mod.Import(typeof(Path).GetMethod("Combine", new[] { typeof(string), typeof(string) }));
            var m4 = mod.Import(typeof(Assembly).GetMethod("LoadFrom", new[] { typeof(string) }));
            var m5 = mod.Import(typeof(Assembly).GetMethod("GetType", new[] { typeof(string) }));
            var m6 = mod.Import(typeof(Type).GetMethod("GetMethod", new[] { typeof(string) }));
            var m7 = mod.Import(typeof(MethodBase).GetMethod("Invoke", new[] { typeof(object), typeof(object[]) }));

            int targetIndex = -1;
            for (int i = 0; i < onOpening.Body.Instructions.Count; i++)
            {
                var instr = onOpening.Body.Instructions[i];
                if (instr.OpCode == OpCodes.Call && instr.Operand?.ToString().Contains("OnDropDownOpening") == true)
                {
                    targetIndex = i - 2;
                    break;
                }
            }

            if (targetIndex < 0)
            {
                targetIndex = onOpening.Body.Instructions.Count - 1;
            }

            var dynamicInstrs = new List<Instruction>
            {
                Instruction.Create(OpCodes.Call, m1),
                Instruction.Create(OpCodes.Callvirt, m2),
                Instruction.Create(OpCodes.Ldstr, CompanionDllName),
                Instruction.Create(OpCodes.Call, m3),
                Instruction.Create(OpCodes.Call, m4),
                Instruction.Create(OpCodes.Ldstr, ExporterTypeName),
                Instruction.Create(OpCodes.Callvirt, m5),
                Instruction.Create(OpCodes.Ldstr, "OnDropDownOpening"),
                Instruction.Create(OpCodes.Callvirt, m6),
                Instruction.Create(OpCodes.Ldnull),
                Instruction.Create(OpCodes.Ldc_I4_1),
                Instruction.Create(OpCodes.Newarr, mod.CorLibTypes.Object),
                Instruction.Create(OpCodes.Dup),
                Instruction.Create(OpCodes.Ldc_I4_0),
                Instruction.Create(OpCodes.Ldarg_0),
                Instruction.Create(OpCodes.Stelem_Ref),
                Instruction.Create(OpCodes.Callvirt, m7),
                Instruction.Create(OpCodes.Pop)
            };

            for (int i = 0; i < dynamicInstrs.Count; i++)
            {
                onOpening.Body.Instructions.Insert(targetIndex + i, dynamicInstrs[i]);
            }
            Console.WriteLine(" -> Injected FileMenu hook successfully");
        }

        private static void WritePatchedModule(ModuleDefMD mod, string outputPath)
        {
            var options = new ModuleWriterOptions(mod);
            options.Win32Resources = new Win32ResourcesPE(mod.Metadata.PEImage);
            options.PEHeadersOptions.Subsystem = Subsystem.WindowsGui;
            options.Cor20HeaderOptions.Flags = dnlib.DotNet.MD.ComImageFlags.ILOnly;
            options.MetadataOptions.Flags = MetadataFlags.PreserveAll;
            mod.Write(outputPath, options);
        }

        private static void DeployPatch(ModuleDefMD mod, byte[] companionBytes, byte[] sourceBytes, string sourceDll, string targetDir, bool isSourcePatched)
        {
            string stagingDir = Path.Combine(AppContext.BaseDirectory, "patched_output");
            Directory.CreateDirectory(stagingDir);

            string stagedDll = Path.Combine(stagingDir, "paintdotnet.dll");
            string stagedCompanion = Path.Combine(stagingDir, CompanionDllName);
            string stagedOrig = Path.Combine(stagingDir, "paintdotnet.orig.dll");

            Console.WriteLine("Preparing patch files in staging folder (patched_output)...");
            WritePatchedModule(mod, stagedDll);
            Console.WriteLine("  [+] Created patched paintdotnet.dll");
            File.WriteAllBytes(stagedCompanion, companionBytes);
            Console.WriteLine($"  [+] Prepared companion {CompanionDllName}");

            if (!isSourcePatched && !string.IsNullOrEmpty(sourceDll) && File.Exists(sourceDll))
            {
                try
                {
                    File.Copy(sourceDll, stagedOrig, true);
                    Console.WriteLine("  [+] Preserved vanilla backup paintdotnet.orig.dll (original timestamps preserved)");
                }
                catch
                {
                    if (sourceBytes != null)
                    {
                        File.WriteAllBytes(stagedOrig, sourceBytes);
                        Console.WriteLine("  [+] Preserved vanilla backup paintdotnet.orig.dll");
                    }
                }
            }

            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("Target Paint.NET installation:");
            Console.WriteLine($"  {targetDir}");
            Console.ResetColor();
            Console.WriteLine();
            Console.WriteLine("Choose an installation option:");
            Console.WriteLine("  [1] Install automatically (Default - prompts for admin elevation if needed)");
            Console.WriteLine("  [2] Open folders in File Explorer to inspect / copy manually");
            Console.WriteLine("  [3] Cancel (Leaves Paint.NET completely untouched)");
            Console.Write("Enter choice [1/2/3]: ");
            string choice = Console.ReadLine()?.Trim();

            if (choice == "3" || choice?.Equals("cancel", StringComparison.OrdinalIgnoreCase) == true || choice?.Equals("c", StringComparison.OrdinalIgnoreCase) == true)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine();
                Console.WriteLine("Installation cancelled. Paint.NET was not modified.");
                Console.WriteLine($"Staged files remain available in:\n  {stagingDir}");
                Console.ResetColor();
                return;
            }

            if (choice == "2")
            {
                ShowManualInstructions(stagingDir, targetDir);
                return;
            }

            // Option 1: Automatic install
            bool canWriteDirectly = CanWriteToDirectory(targetDir);
            if (canWriteDirectly)
            {
                Console.WriteLine("Applying files to Paint.NET directory...");
                try
                {
                    CopyStagedFilesToTarget(stagingDir, targetDir);
                    PrintSuccessMessage();
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"Direct install failed: {ex.Message}");
                    Console.ResetColor();
                    ShowManualInstructions(stagingDir, targetDir);
                }
            }
            else
            {
                Console.WriteLine("Requesting Administrator permissions to install...");
                try
                {
                    string exePath = Process.GetCurrentProcess().MainModule?.FileName;
                    if (!string.IsNullOrEmpty(exePath))
                    {
                        var psi = new ProcessStartInfo
                        {
                            FileName = exePath,
                            Arguments = $"--install \"{targetDir}\" \"{stagingDir}\"",
                            Verb = "runas",
                            UseShellExecute = true
                        };
                        var proc = Process.Start(psi);
                        proc?.WaitForExit();
                        if (proc?.ExitCode == 0)
                        {
                            return;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"Automatic elevation was cancelled or failed: {ex.Message}");
                    Console.ResetColor();
                }

                ShowManualInstructions(stagingDir, targetDir);
            }
        }

        private static void CopyStagedFilesToTarget(string stagingDir, string targetDir)
        {
            string origDll = Path.Combine(targetDir, "paintdotnet.orig.dll");
            string targetDll = Path.Combine(targetDir, "paintdotnet.dll");
            string stagedOrig = Path.Combine(stagingDir, "paintdotnet.orig.dll");

            if (!File.Exists(origDll))
            {
                if (File.Exists(stagedOrig))
                {
                    File.Copy(stagedOrig, origDll, true);
                }
                else if (File.Exists(targetDll) && !IsAssemblyPatched(targetDll))
                {
                    File.Copy(targetDll, origDll, false);
                }
            }
            else if (File.Exists(stagedOrig) && !IsAssemblyPatched(stagedOrig))
            {
                File.Copy(stagedOrig, origDll, true);
            }

            File.Copy(Path.Combine(stagingDir, "paintdotnet.dll"), targetDll, true);
            File.Copy(Path.Combine(stagingDir, CompanionDllName), Path.Combine(targetDir, CompanionDllName), true);
        }

        private static void PrintSuccessMessage()
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine();
            Console.WriteLine("==================================================================");
            Console.WriteLine("   SUCCESS! Paint.NET has been patched with Quick Export!         ");
            Console.WriteLine("==================================================================");
            Console.ResetColor();
            Console.WriteLine("Features added to File Menu:");
            Console.WriteLine("  - Quick Export as PNG  (Ctrl + Alt + G  or  Alt + F, G)");
            Console.WriteLine("  - Quick Export as JPEG (Ctrl + Alt + J  or  Alt + F, J)");
            Console.WriteLine();
        }

        private static void ShowManualInstructions(string stagingDir, string targetDir)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("Manual Instructions:");
            Console.WriteLine("1. Copy all files from:");
            Console.WriteLine($"   {stagingDir}");
            Console.WriteLine("2. Paste and overwrite into:");
            Console.WriteLine($"   {targetDir}");
            Console.ResetColor();

            try
            {
                Process.Start("explorer.exe", stagingDir);
                Process.Start("explorer.exe", targetDir);
            }
            catch { }
        }

        private static int RunElevatedInstall(string targetDir, string stagingDir)
        {
            try
            {
                CopyStagedFilesToTarget(stagingDir, targetDir);
                PrintSuccessMessage();
                return 0;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Installation error: {ex.Message}");
                Console.ResetColor();
                Pause();
                return 1;
            }
        }

        private static int RestoreOriginal(string targetDir, string dllPath, string origDllPath)
        {
            Console.WriteLine("Restoring original Paint.NET files...");
            try
            {
                string companion = Path.Combine(targetDir, CompanionDllName);
                if (CanWriteToDirectory(targetDir))
                {
                    File.Move(origDllPath, dllPath, true);
                    if (File.Exists(companion)) File.Delete(companion);
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("Restored original paintdotnet.dll successfully!");
                    Console.ResetColor();
                }
                else
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/c move /Y \"{origDllPath}\" \"{dllPath}\" & if exist \"{companion}\" del /f /q \"{companion}\"",
                        Verb = "runas",
                        UseShellExecute = true
                    };
                    Process.Start(psi)?.WaitForExit();
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("Restored original paintdotnet.dll successfully!");
                    Console.ResetColor();
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Failed to restore: {ex.Message}");
                Console.ResetColor();
            }
            Pause();
            return 0;
        }

        private static byte[] GetCompanionDllBytes()
        {
            // Try adjacent file first (unified bin/Debug or bin/Release)
            string localFile = Path.Combine(AppContext.BaseDirectory, CompanionDllName);
            if (File.Exists(localFile))
            {
                return File.ReadAllBytes(localFile);
            }

            // Try embedded resource (single-file distribution)
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(CompanionDllName);
            if (stream != null)
            {
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                return ms.ToArray();
            }

            return null;
        }

        private static bool IsValidPaintDotNetDirectory(string dir)
        {
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return false;
            bool hasExe = File.Exists(Path.Combine(dir, "paintdotnet.exe"));
            bool hasDll = File.Exists(Path.Combine(dir, "paintdotnet.dll")) || File.Exists(Path.Combine(dir, "paintdotnet.orig.dll"));
            return hasExe && hasDll;
        }

        private static string FindPaintDotNetDirectory()
        {
            // 1. Standard Program Files installation (canonical location)
            string progFiles = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "paint.net");
            if (IsValidPaintDotNetDirectory(progFiles))
            {
                return progFiles;
            }

            // 2. Registry App Paths
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\paintdotnet.exe");
                var path = key?.GetValue(null) as string;
                if (!string.IsNullOrEmpty(path))
                {
                    string dir = Path.GetDirectoryName(path);
                    if (IsValidPaintDotNetDirectory(dir))
                    {
                        return dir;
                    }
                }
            }
            catch { }

            // 3. Application base directory (if patcher is placed directly in Paint.NET folder)
            if (IsValidPaintDotNetDirectory(AppContext.BaseDirectory))
            {
                return AppContext.BaseDirectory;
            }

            // 4. Current working directory
            if (IsValidPaintDotNetDirectory(Environment.CurrentDirectory))
            {
                return Environment.CurrentDirectory;
            }

            // 5. Parent directory (only if it contains a valid Paint.NET installation with paintdotnet.exe)
            string parent = Path.GetDirectoryName(AppContext.BaseDirectory);
            if (!string.IsNullOrEmpty(parent) && IsValidPaintDotNetDirectory(parent))
            {
                return parent;
            }

            return null;
        }

        private static bool CanWriteToDirectory(string dir)
        {
            try
            {
                string testFile = Path.Combine(dir, $"__pdn_write_test_{Guid.NewGuid():N}.tmp");
                File.WriteAllText(testFile, "test");
                File.Delete(testFile);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void EnsurePaintDotNetClosed()
        {
            while (Process.GetProcessesByName("paintdotnet").Length > 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("Paint.NET is currently running!");
                Console.Write("Please close Paint.NET, then press [Enter] to continue...");
                Console.ResetColor();
                Console.ReadLine();
            }
        }

        private static void Pause()
        {
            Console.WriteLine();
            Console.WriteLine("Press any key to exit...");
            try { Console.ReadKey(); } catch { }
        }

        private static bool IsAssemblyPatched(string filePath)
        {
            try
            {
                if (!File.Exists(filePath)) return false;
                byte[] bytes = File.ReadAllBytes(filePath);
                using var mod = ModuleDefMD.Load(bytes);
                return IsAssemblyPatched(mod);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsAssemblyPatched(ModuleDefMD mod)
        {
            try
            {
                var fileMenu = mod.Find("PaintDotNet.Menus.FileMenu", true);
                var onOpening = fileMenu?.FindMethod("OnDropDownOpening");
                if (onOpening?.Body == null) return false;

                return onOpening.Body.Instructions.Any(i =>
                    i.OpCode == OpCodes.Ldstr && (i.Operand as string) == CompanionDllName);
            }
            catch
            {
                return false;
            }
        }
    }
}
