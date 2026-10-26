using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace EchoCore.Bootstrapper
{
    internal sealed class SelfUpdater
    {
        internal static string DefaultRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EchoCore");
        private readonly string root;
        private string Updates => Path.Combine(root, "Updates");
        private string Installed => Path.Combine(root, "EchoCoreBootstrapper.exe");
        internal SelfUpdater(string directory) { root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar); }
        private void CheckDirectories()
        {
            foreach(var path in new[]{root,Updates})
                if(Directory.Exists(path) && (File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Invalid update directory");
            if(File.Exists(Installed) && (File.GetAttributes(Installed)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Invalid installed launcher");
        }

        internal static bool Offered(Version current, string version, string file, string hash, long size)
        {
            return Version.TryParse(version, out var candidate) && Normalize(candidate) > Normalize(current) &&
                size > 0 && size <= 4 * 1024 * 1024 && hash != null && hash.Length == 64 && hash.All(Uri.IsHexDigit) &&
                !string.IsNullOrEmpty(file) && file.Length <= 100 && file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                file.All(c => c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' || c == '.' || c == '-' || c == '_') && !file.Contains("..");
        }
        private static Version Normalize(Version value) => new Version(value.Major, value.Minor, Math.Max(0,value.Build), Math.Max(0,value.Revision));
        private static string Hash(string path)
        {
            using(var algorithm=SHA256.Create()) using(var stream=File.OpenRead(path))
                return string.Concat(algorithm.ComputeHash(stream).Select(b=>b.ToString("x2")));
        }
        private static FileSecurity PrivateFileSecurity()
        {
            var owner=WindowsIdentity.GetCurrent().User;
            var security=new FileSecurity();security.SetAccessRuleProtection(true,false);security.SetOwner(owner);
            security.AddAccessRule(new FileSystemAccessRule(owner,FileSystemRights.FullControl,AccessControlType.Allow));
            return security;
        }
        private static void WriteState(string path, State state)
        {
            using(var stream=new FileStream(path,FileMode.Create,FileSystemRights.Write,FileShare.None,4096,FileOptions.None,PrivateFileSecurity()))
            using(var writer=new StreamWriter(stream,new UTF8Encoding(false)))writer.Write(new JavaScriptSerializer().Serialize(state));
        }
        private State ReadState(string path)
        {
            CheckDirectories();
            path=Path.GetFullPath(path);
            var directory=Path.GetDirectoryName(path);
            if (!string.Equals(Path.GetDirectoryName(directory),Updates,StringComparison.OrdinalIgnoreCase) ||
                !Guid.TryParseExact(Path.GetFileName(directory),"N",out _) || Path.GetFileName(path)!="state.json" ||
                (File.GetAttributes(directory)&FileAttributes.ReparsePoint)!=0 || new FileInfo(path).Length>65536)
                throw new InvalidOperationException("Invalid update location");
            var acl=File.GetAccessControl(path);var owner=WindowsIdentity.GetCurrent().User;
            if(!acl.AreAccessRulesProtected || !acl.GetOwner(typeof(SecurityIdentifier)).Equals(owner))throw new InvalidOperationException("Invalid update permissions");
            foreach(FileSystemAccessRule rule in acl.GetAccessRules(true,true,typeof(SecurityIdentifier)))
                if(!rule.IdentityReference.Equals(owner) || rule.IsInherited)throw new InvalidOperationException("Invalid update permissions");
            var state=new JavaScriptSerializer().Deserialize<State>(File.ReadAllText(path));
            if(state==null || state.Arguments==null || state.Arguments.Length>16 || state.Arguments.Any(a=>a==null||a.Length>8192) ||
                state.Hash==null || state.Hash.Length!=64 || !state.Hash.All(Uri.IsHexDigit))throw new InvalidOperationException("Invalid update state");
            return state;
        }

        internal async Task<bool> TryUpdateAsync(HttpClient http, string version, string file, string hash, long size, string[] arguments, Action<string> status)
        {
            if(!Offered(Assembly.GetExecutingAssembly().GetName().Version,version,file,hash,size))return false;
            string directory=null;bool handedOff=false;
            try
            {
                CheckDirectories();
                Directory.CreateDirectory(Updates);
                directory=Path.Combine(Updates,Guid.NewGuid().ToString("N"));
                var security=new DirectorySecurity();var owner=WindowsIdentity.GetCurrent().User;
                security.SetAccessRuleProtection(true,false);security.SetOwner(owner);
                security.AddAccessRule(new FileSystemAccessRule(owner,FileSystemRights.FullControl,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));
                new DirectoryInfo(directory).Create(security);
                var executable=Path.Combine(directory,"EchoCoreBootstrapper.exe");
                status("Updating EchoCore...");
                using(var timeout=new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(30)))
                using(var response=await http.GetAsync("https://www.echocr.xyz/setup/"+file,HttpCompletionOption.ResponseHeadersRead,timeout.Token))
                {
                    response.EnsureSuccessStatusCode();
                    using(var input=await response.Content.ReadAsStreamAsync()) using(var output=File.Create(executable))
                    {
                        var buffer=new byte[32768];long received=0;int count;
                        while((count=await input.ReadAsync(buffer,0,buffer.Length,timeout.Token))>0)
                        {
                            received+=count;if(received>size)throw new InvalidDataException("Update size mismatch");
                            await output.WriteAsync(buffer,0,count,timeout.Token);
                        }
                        if(received!=size)throw new InvalidDataException("Update size mismatch");
                    }
                }
                if(!string.Equals(Hash(executable),hash,StringComparison.OrdinalIgnoreCase) ||
                    Normalize(AssemblyName.GetAssemblyName(executable).Version)!=Normalize(Version.Parse(version)))throw new InvalidDataException("Update verification failed");
                var path=Path.Combine(directory,"state.json");
                WriteState(path,new State {Hash=hash,Arguments=arguments,ParentId=Process.GetCurrentProcess().Id,ParentPath=Assembly.GetExecutingAssembly().Location});
                Start(executable,new[]{"--apply-update",path});handedOff=true;return true;
            }
            catch(Exception error) when(error is IOException || error is InvalidDataException || error is UnauthorizedAccessException || error is HttpRequestException ||
                error is OperationCanceledException || error is System.ComponentModel.Win32Exception || error is BadImageFormatException || error is System.Security.SecurityException)
            {return false;}
            finally
            {
                if(!handedOff && directory!=null)
                {
                    foreach(var name in new[]{"state.json","EchoCoreBootstrapper.exe"})try{File.Delete(Path.Combine(directory,name));}catch(IOException){}catch(UnauthorizedAccessException){}
                    try{Directory.Delete(directory);}catch(IOException){}catch(UnauthorizedAccessException){}
                }
            }
        }
        private static void Start(string executable,string[] arguments)
        {
            using(var process=Process.Start(new ProcessStartInfo {FileName=executable,Arguments=CommandLine.Join(arguments),
                WorkingDirectory=Path.GetDirectoryName(executable),UseShellExecute=false,CreateNoWindow=true})){}
        }
        private static void WaitFor(int pid,string executable,int milliseconds)
        {
            if(pid<=0)return;
            try
            {
                using(var process=Process.GetProcessById(pid))
                    if(string.Equals(process.MainModule.FileName,executable,StringComparison.OrdinalIgnoreCase) && !process.WaitForExit(milliseconds))
                        throw new IOException("The previous installer is still running");
            }
            catch(ArgumentException){}
            catch(InvalidOperationException){}
        }
        internal void Apply(string path)
        {
            var state=ReadState(path);
            var self=Assembly.GetExecutingAssembly().Location;
            if(!string.Equals(self,Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path)),"EchoCoreBootstrapper.exe"),StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(Hash(self),state.Hash,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Invalid update helper");
            var started=false;string temporary=null;
            try
            {
                WaitFor(state.ParentId,state.ParentPath,30000);
                CheckDirectories();
                temporary=Path.Combine(root,"bootstrapper-"+Guid.NewGuid().ToString("N")+".updating");
                var backup=Path.Combine(root,"EchoCoreBootstrapper.previous.exe");
                using(var input=File.OpenRead(self))using(var output=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None))input.CopyTo(output);
                if(!string.Equals(Hash(temporary),state.Hash,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Update copy failed");
                if(File.Exists(Installed)) {if(File.Exists(backup))File.Delete(backup);File.Replace(temporary,Installed,backup);}
                else File.Move(temporary,Installed);
                state.HelperId=Process.GetCurrentProcess().Id;WriteState(path,state);
                Start(Installed,new[]{"--resume-update",path});started=true;
            }
            finally
            {
                if(!started)try{File.Delete(path);}catch(IOException){}catch(UnauthorizedAccessException){}
                if(temporary!=null)try{File.Delete(temporary);}catch(IOException){}catch(UnauthorizedAccessException){}
            }
        }
        internal string[] Resume(string path)
        {
            var state=ReadState(path);
            if(!string.Equals(Assembly.GetExecutingAssembly().Location,Installed,StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(Hash(Installed),state.Hash,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Invalid installed update");
            var directory=Path.GetDirectoryName(Path.GetFullPath(path));
            File.Delete(path);
            WaitFor(state.HelperId,Path.Combine(directory,"EchoCoreBootstrapper.exe"),10000);
            try{File.Delete(Path.Combine(directory,"EchoCoreBootstrapper.exe"));Directory.Delete(directory);}catch(IOException){}catch(UnauthorizedAccessException){}
            return state.Arguments;
        }
        internal sealed class State
        {
            public string Hash {get;set;}
            public string[] Arguments {get;set;}
            public int ParentId {get;set;}
            public string ParentPath {get;set;}
            public int HelperId {get;set;}
        }
    }
}
