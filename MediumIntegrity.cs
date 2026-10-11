using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace EchoCore.Bootstrapper
{
    internal static class MediumIntegrity
    {
        private const uint TokenDuplicate = 0x0002;
        private const uint TokenQuery = 0x0008;
        private const uint TokenAssignPrimary = 0x0001;
        private const uint TokenAdjustDefault = 0x0080;
        private const uint MaximumAllowed = 0x02000000;
        private const int SecurityImpersonation = 2;
        private const int TokenPrimary = 1;
        private const int TokenIntegrityLevel = 25;
        private const int SeGroupIntegrity = 0x20;
        private const int HighIntegrity = 0x3000;
        private const string MediumIntegritySid = "S-1-16-8192";

        [StructLayout(LayoutKind.Sequential)]
        private struct TokenMandatoryLabel
        {
            public IntPtr Sid;
            public int Attributes;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct StartupInfo
        {
            public int cb;
            public string lpReserved;
            public string lpDesktop;
            public string lpTitle;
            public int dwX;
            public int dwY;
            public int dwXSize;
            public int dwYSize;
            public int dwXCountChars;
            public int dwYCountChars;
            public int dwFillAttribute;
            public int dwFlags;
            public short wShowWindow;
            public short cbReserved2;
            public IntPtr lpReserved2;
            public IntPtr hStdInput;
            public IntPtr hStdOutput;
            public IntPtr hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ProcessInformation
        {
            public IntPtr hProcess;
            public IntPtr hThread;
            public int dwProcessId;
            public int dwThreadId;
        }

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool DuplicateTokenEx(IntPtr token, uint access, IntPtr attributes, int level, int type, out IntPtr newToken);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool GetTokenInformation(IntPtr token, int infoClass, IntPtr info, int length, out int returned);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool SetTokenInformation(IntPtr token, int infoClass, ref TokenMandatoryLabel info, int length);

        [DllImport("advapi32.dll")]
        private static extern IntPtr GetSidSubAuthority(IntPtr sid, uint index);

        [DllImport("advapi32.dll")]
        private static extern IntPtr GetSidSubAuthorityCount(IntPtr sid);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CreateProcessAsUser(IntPtr token, string application, string commandLine, IntPtr processAttributes,
            IntPtr threadAttributes, bool inheritHandles, uint flags, IntPtr environment, string directory,
            ref StartupInfo startupInfo, out ProcessInformation processInformation);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);

        public static bool IsHigh()
        {
            if (!OpenProcessToken(Process.GetCurrentProcess().Handle, TokenQuery, out var token))
                return false;
            var buffer = Marshal.AllocHGlobal(256);
            try
            {
                if (!GetTokenInformation(token, TokenIntegrityLevel, buffer, 256, out _))
                    return false;
                var sid = Marshal.ReadIntPtr(buffer);
                var count = Marshal.ReadByte(GetSidSubAuthorityCount(sid));
                return Marshal.ReadInt32(GetSidSubAuthority(sid, (uint)(count - 1))) >= HighIntegrity;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
                CloseHandle(token);
            }
        }

        public static void Start(string application, string commandLine, string directory)
        {
            if (!OpenProcessToken(Process.GetCurrentProcess().Handle, TokenDuplicate | TokenQuery | TokenAssignPrimary | TokenAdjustDefault, out var token))
                throw new Win32Exception();
            var primary = IntPtr.Zero;
            var sid = new SecurityIdentifier(MediumIntegritySid);
            var sidBytes = new byte[sid.BinaryLength];
            sid.GetBinaryForm(sidBytes, 0);
            var sidPointer = Marshal.AllocHGlobal(sidBytes.Length);
            try
            {
                if (!DuplicateTokenEx(token, MaximumAllowed, IntPtr.Zero, SecurityImpersonation, TokenPrimary, out primary))
                    throw new Win32Exception();
                Marshal.Copy(sidBytes, 0, sidPointer, sidBytes.Length);
                var label = new TokenMandatoryLabel { Sid = sidPointer, Attributes = SeGroupIntegrity };
                if (!SetTokenInformation(primary, TokenIntegrityLevel, ref label, Marshal.SizeOf(label) + sidBytes.Length))
                    throw new Win32Exception();
                var startup = new StartupInfo { cb = Marshal.SizeOf(typeof(StartupInfo)) };
                if (!CreateProcessAsUser(primary, application, commandLine, IntPtr.Zero, IntPtr.Zero, false, 0, IntPtr.Zero,
                        directory, ref startup, out var info))
                    throw new Win32Exception();
                CloseHandle(info.hThread);
                CloseHandle(info.hProcess);
            }
            finally
            {
                Marshal.FreeHGlobal(sidPointer);
                if (primary != IntPtr.Zero)
                    CloseHandle(primary);
                CloseHandle(token);
            }
        }
    }
}
