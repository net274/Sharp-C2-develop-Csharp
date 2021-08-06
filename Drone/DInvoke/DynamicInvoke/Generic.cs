﻿// Author: Ryan Cobb (@cobbr_io)
// Project: SharpSploit (https://github.com/cobbr/SharpSploit)
// License: BSD 3-Clause

using System;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Drone.DInvoke.DynamicInvoke
{
    public static class Generic
    {
        public static object DynamicAPIInvoke(string DLLName, string FunctionName, Type FunctionDelegateType, ref object[] Parameters, bool CanLoadFromDisk = false, bool ResolveForwards = true)
        {
            IntPtr pFunction = GetLibraryAddress(DLLName, FunctionName, CanLoadFromDisk, ResolveForwards);
            return DynamicFunctionInvoke(pFunction, FunctionDelegateType, ref Parameters);
        }

        public static object DynamicFunctionInvoke(IntPtr FunctionPointer, Type FunctionDelegateType, ref object[] Parameters)
        {
            Delegate funcDelegate = Marshal.GetDelegateForFunctionPointer(FunctionPointer, FunctionDelegateType);
            return funcDelegate.DynamicInvoke(Parameters);
        }

        public static IntPtr LoadModuleFromDisk(string DLLPath)
        {
            Data.Native.UNICODE_STRING uModuleName = new Data.Native.UNICODE_STRING();
            Native.RtlInitUnicodeString(ref uModuleName, DLLPath);

            IntPtr hModule = IntPtr.Zero;
            Data.Native.NTSTATUS CallResult = Native.LdrLoadDll(IntPtr.Zero, 0, ref uModuleName, ref hModule);
            if (CallResult != Data.Native.NTSTATUS.Success || hModule == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            return hModule;
        }

        public static IntPtr GetLibraryAddress(string DLLName, string FunctionName, bool CanLoadFromDisk = false, bool ResolveForwards = true)
        {
            IntPtr hModule = GetLoadedModuleAddress(DLLName);
            if (hModule == IntPtr.Zero && CanLoadFromDisk)
            {
                hModule = LoadModuleFromDisk(DLLName);
                if (hModule == IntPtr.Zero)
                {
                    throw new FileNotFoundException(DLLName + ", unable to find the specified file.");
                }
            }
            else if (hModule == IntPtr.Zero)
            {
                throw new DllNotFoundException(DLLName + ", Dll was not found.");
            }

            return GetExportAddress(hModule, FunctionName, ResolveForwards);
        }

        public static IntPtr GetLoadedModuleAddress(string DLLName)
        {
            ProcessModuleCollection ProcModules = Process.GetCurrentProcess().Modules;
            foreach (ProcessModule Mod in ProcModules)
            {
                if (Mod.FileName.ToLower().EndsWith(DLLName.ToLower()))
                {
                    return Mod.BaseAddress;
                }
            }
            return IntPtr.Zero;
        }

        public static IntPtr GetPebLdrModuleEntry(string DLLName)
        {
            // Get _PEB pointer
            Data.Native.PROCESS_BASIC_INFORMATION pbi = Native.NtQueryInformationProcessBasicInformation((IntPtr)(-1));

            // Set function variables
            UInt32 LdrDataOffset = 0;
            UInt32 InLoadOrderModuleListOffset = 0;
            if (IntPtr.Size == 4)
            {
                LdrDataOffset = 0xc;
                InLoadOrderModuleListOffset = 0xC;
            }
            else
            {
                LdrDataOffset = 0x18;
                InLoadOrderModuleListOffset = 0x10;
            }

            // Get module InLoadOrderModuleList -> _LIST_ENTRY
            IntPtr PEB_LDR_DATA = Marshal.ReadIntPtr((IntPtr)((UInt64)pbi.PebBaseAddress + LdrDataOffset));
            IntPtr pInLoadOrderModuleList = (IntPtr)((UInt64)PEB_LDR_DATA + InLoadOrderModuleListOffset);
            Data.Native.LIST_ENTRY le = (Data.Native.LIST_ENTRY)Marshal.PtrToStructure(pInLoadOrderModuleList, typeof(Data.Native.LIST_ENTRY));

            // Loop entries
            IntPtr flink = le.Flink;
            IntPtr hModule = IntPtr.Zero;
            Data.PE.LDR_DATA_TABLE_ENTRY dte = (Data.PE.LDR_DATA_TABLE_ENTRY)Marshal.PtrToStructure(flink, typeof(Data.PE.LDR_DATA_TABLE_ENTRY));
            while (dte.InLoadOrderLinks.Flink != le.Blink)
            {
                // Match module name
                if (Marshal.PtrToStringUni(dte.FullDllName.Buffer).EndsWith(DLLName, StringComparison.OrdinalIgnoreCase))
                {
                    hModule = dte.DllBase;
                }
            
                // Move Ptr
                flink = dte.InLoadOrderLinks.Flink;
                dte = (Data.PE.LDR_DATA_TABLE_ENTRY)Marshal.PtrToStructure(flink, typeof(Data.PE.LDR_DATA_TABLE_ENTRY));
            }

            return hModule;
        }

        public static IntPtr GetExportAddress(IntPtr ModuleBase, string ExportName, bool ResolveForwards = true)
        {
            IntPtr FunctionPtr = IntPtr.Zero;
            try
            {
                // Traverse the PE header in memory
                Int32 PeHeader = Marshal.ReadInt32((IntPtr)(ModuleBase.ToInt64() + 0x3C));
                Int16 OptHeaderSize = Marshal.ReadInt16((IntPtr)(ModuleBase.ToInt64() + PeHeader + 0x14));
                Int64 OptHeader = ModuleBase.ToInt64() + PeHeader + 0x18;
                Int16 Magic = Marshal.ReadInt16((IntPtr)OptHeader);
                Int64 pExport = 0;
                if (Magic == 0x010b)
                {
                    pExport = OptHeader + 0x60;
                }
                else
                {
                    pExport = OptHeader + 0x70;
                }

                // Read -> IMAGE_EXPORT_DIRECTORY
                Int32 ExportRVA = Marshal.ReadInt32((IntPtr)pExport);
                Int32 OrdinalBase = Marshal.ReadInt32((IntPtr)(ModuleBase.ToInt64() + ExportRVA + 0x10));
                Int32 NumberOfFunctions = Marshal.ReadInt32((IntPtr)(ModuleBase.ToInt64() + ExportRVA + 0x14));
                Int32 NumberOfNames = Marshal.ReadInt32((IntPtr)(ModuleBase.ToInt64() + ExportRVA + 0x18));
                Int32 FunctionsRVA = Marshal.ReadInt32((IntPtr)(ModuleBase.ToInt64() + ExportRVA + 0x1C));
                Int32 NamesRVA = Marshal.ReadInt32((IntPtr)(ModuleBase.ToInt64() + ExportRVA + 0x20));
                Int32 OrdinalsRVA = Marshal.ReadInt32((IntPtr)(ModuleBase.ToInt64() + ExportRVA + 0x24));

                // Get the VAs of the name table's beginning and end.
                Int64 NamesBegin = ModuleBase.ToInt64() + Marshal.ReadInt32((IntPtr)(ModuleBase.ToInt64() + NamesRVA));
                Int64 NamesFinal = NamesBegin + NumberOfNames * 4;

                // Loop the array of export name RVA's
                for (int i = 0; i < NumberOfNames; i++)
                {
                    string FunctionName = Marshal.PtrToStringAnsi((IntPtr)(ModuleBase.ToInt64() + Marshal.ReadInt32((IntPtr)(ModuleBase.ToInt64() + NamesRVA + i * 4))));
                    
                    if (FunctionName.Equals(ExportName, StringComparison.OrdinalIgnoreCase))
                    {

                        Int32 FunctionOrdinal = Marshal.ReadInt16((IntPtr)(ModuleBase.ToInt64() + OrdinalsRVA + i * 2)) + OrdinalBase;
                        Int32 FunctionRVA = Marshal.ReadInt32((IntPtr)(ModuleBase.ToInt64() + FunctionsRVA + (4 * (FunctionOrdinal - OrdinalBase))));
                        FunctionPtr = (IntPtr)((Int64)ModuleBase + FunctionRVA);
                        
                        if (ResolveForwards == true)
                            // If the export address points to a forward, get the address
                            FunctionPtr = GetForwardAddress(FunctionPtr);

                        break;
                    }
                }
            }
            catch
            {
                // Catch parser failure
                throw new InvalidOperationException("Failed to parse module exports.");
            }

            if (FunctionPtr == IntPtr.Zero)
            {
                // Export not found
                throw new MissingMethodException(ExportName + ", export not found.");
            }
            return FunctionPtr;
        }

        public static IntPtr GetForwardAddress(IntPtr ExportAddress, bool CanLoadFromDisk = false)
        {
            IntPtr FunctionPtr = ExportAddress;
            try
            {
                // Assume it is a forward. If it is not, we will get an error
                string ForwardNames = Marshal.PtrToStringAnsi(FunctionPtr);
                string[] values = ForwardNames.Split('.');

                if (values.Length > 1)
                {
                    string ForwardModuleName = values[0];
                    string ForwardExportName = values[1];

                    // Check if it is an API Set mapping
                    Dictionary<string, string> ApiSet = GetApiSetMapping();
                    string LookupKey = ForwardModuleName.Substring(0, ForwardModuleName.Length - 2) + ".dll";
                    if (ApiSet.ContainsKey(LookupKey))
                        ForwardModuleName = ApiSet[LookupKey];
                    else
                        ForwardModuleName = ForwardModuleName + ".dll";

                    IntPtr hModule = GetPebLdrModuleEntry(ForwardModuleName);
                    if (hModule == IntPtr.Zero && CanLoadFromDisk == true)
                        hModule = LoadModuleFromDisk(ForwardModuleName);
                    if (hModule != IntPtr.Zero)
                    {
                        FunctionPtr = GetExportAddress(hModule, ForwardExportName);
                    }
                }
            }
            catch
            {
                // Do nothing, it was not a forward
            }
            return FunctionPtr;
        }

        public static IntPtr GetNativeExportAddress(IntPtr ModuleBase, string ExportName)
        {
            Data.Native.ANSI_STRING aFunc = new Data.Native.ANSI_STRING
            {
                Length = (ushort)ExportName.Length,
                MaximumLength = (ushort)(ExportName.Length + 2),
                Buffer = Marshal.StringToCoTaskMemAnsi(ExportName)
            };

            IntPtr pAFunc = Marshal.AllocHGlobal(Marshal.SizeOf(aFunc));
            Marshal.StructureToPtr(aFunc, pAFunc, true);

            IntPtr pFuncAddr = IntPtr.Zero;
            Native.LdrGetProcedureAddress(ModuleBase, pAFunc, IntPtr.Zero, ref pFuncAddr);

            Marshal.FreeHGlobal(pAFunc);

            return pFuncAddr;
        }

        public static IntPtr GetNativeExportAddress(IntPtr ModuleBase, short Ordinal)
        {
            IntPtr pFuncAddr = IntPtr.Zero;
            IntPtr pOrd = (IntPtr)Ordinal;

            Native.LdrGetProcedureAddress(ModuleBase, IntPtr.Zero, pOrd, ref pFuncAddr);

            return pFuncAddr;
        }

        public static Data.PE.PE_META_DATA GetPeMetaData(IntPtr pModule)
        {
            Data.PE.PE_META_DATA PeMetaData = new Data.PE.PE_META_DATA();
            try
            {
                UInt32 e_lfanew = (UInt32)Marshal.ReadInt32((IntPtr)((UInt64)pModule + 0x3c));
                PeMetaData.Pe = (UInt32)Marshal.ReadInt32((IntPtr)((UInt64)pModule + e_lfanew));
                // Validate PE signature
                if (PeMetaData.Pe != 0x4550)
                {
                    throw new InvalidOperationException("Invalid PE signature.");
                }
                PeMetaData.ImageFileHeader = (Data.PE.IMAGE_FILE_HEADER)Marshal.PtrToStructure((IntPtr)((UInt64)pModule + e_lfanew + 0x4), typeof(Data.PE.IMAGE_FILE_HEADER));
                IntPtr OptHeader = (IntPtr)((UInt64)pModule + e_lfanew + 0x18);
                UInt16 PEArch = (UInt16)Marshal.ReadInt16(OptHeader);
                // Validate PE arch
                if (PEArch == 0x010b) // Image is x32
                {
                    PeMetaData.Is32Bit = true;
                    PeMetaData.OptHeader32 = (Data.PE.IMAGE_OPTIONAL_HEADER32)Marshal.PtrToStructure(OptHeader, typeof(Data.PE.IMAGE_OPTIONAL_HEADER32));
                }
                else if (PEArch == 0x020b) // Image is x64
                {
                    PeMetaData.Is32Bit = false;
                    PeMetaData.OptHeader64 = (Data.PE.IMAGE_OPTIONAL_HEADER64)Marshal.PtrToStructure(OptHeader, typeof(Data.PE.IMAGE_OPTIONAL_HEADER64));
                } else
                {
                    throw new InvalidOperationException("Invalid magic value (PE32/PE32+).");
                }
                // Read sections
                Data.PE.IMAGE_SECTION_HEADER[] SectionArray = new Data.PE.IMAGE_SECTION_HEADER[PeMetaData.ImageFileHeader.NumberOfSections];
                for (int i = 0; i < PeMetaData.ImageFileHeader.NumberOfSections; i++)
                {
                    IntPtr SectionPtr = (IntPtr)((UInt64)OptHeader + PeMetaData.ImageFileHeader.SizeOfOptionalHeader + (UInt32)(i * 0x28));
                    SectionArray[i] = (Data.PE.IMAGE_SECTION_HEADER)Marshal.PtrToStructure(SectionPtr, typeof(Data.PE.IMAGE_SECTION_HEADER));
                }
                PeMetaData.Sections = SectionArray;
            }
            catch
            {
                throw new InvalidOperationException("Invalid module base specified.");
            }
            return PeMetaData;
        }

        public static Dictionary<string, string> GetApiSetMapping()
        {
            Data.Native.PROCESS_BASIC_INFORMATION pbi = Native.NtQueryInformationProcessBasicInformation((IntPtr)(-1));
            UInt32 ApiSetMapOffset = IntPtr.Size == 4 ? (UInt32)0x38 : 0x68;

            // Create mapping dictionary
            Dictionary<string, string> ApiSetDict = new Dictionary<string, string>();

            IntPtr pApiSetNamespace = Marshal.ReadIntPtr((IntPtr)((UInt64)pbi.PebBaseAddress + ApiSetMapOffset));
            Data.PE.ApiSetNamespace Namespace = (Data.PE.ApiSetNamespace)Marshal.PtrToStructure(pApiSetNamespace, typeof(Data.PE.ApiSetNamespace));
            for (var i = 0; i < Namespace.Count; i++)
            {
                Data.PE.ApiSetNamespaceEntry SetEntry = new Data.PE.ApiSetNamespaceEntry();
                IntPtr pSetEntry = (IntPtr)((UInt64)pApiSetNamespace + (UInt64)Namespace.EntryOffset + (UInt64)(i * Marshal.SizeOf(SetEntry)));
                SetEntry = (Data.PE.ApiSetNamespaceEntry)Marshal.PtrToStructure(pSetEntry, typeof(Data.PE.ApiSetNamespaceEntry));

                string ApiSetEntryName = Marshal.PtrToStringUni((IntPtr)((UInt64)pApiSetNamespace + (UInt64)SetEntry.NameOffset), SetEntry.NameLength/2);
                string ApiSetEntryKey = ApiSetEntryName.Substring(0, ApiSetEntryName.Length - 2) + ".dll" ; // Remove the patch number and add .dll

                Data.PE.ApiSetValueEntry SetValue = new Data.PE.ApiSetValueEntry();

                IntPtr pSetValue = IntPtr.Zero;

                // If there is only one host, then use it
                if (SetEntry.ValueLength == 1)
                    pSetValue = (IntPtr)((UInt64)pApiSetNamespace + (UInt64)SetEntry.ValueOffset);
                else if (SetEntry.ValueLength > 1)
                {
                    // Loop through the hosts until we find one that is different from the key, if available
                    for (var j = 0; j < SetEntry.ValueLength; j++)
                    {
                        IntPtr host = (IntPtr)((UInt64)pApiSetNamespace + (UInt64)SetEntry.ValueOffset + (UInt64)Marshal.SizeOf(SetValue) * (UInt64)j);
                        if (Marshal.PtrToStringUni(host) != ApiSetEntryName)
                            pSetValue = (IntPtr)((UInt64)pApiSetNamespace + (UInt64)SetEntry.ValueOffset + (UInt64)Marshal.SizeOf(SetValue) * (UInt64)j);
                    }
                    // If there is not one different from the key, then just use the key and hope that works
                    if (pSetValue == IntPtr.Zero)
                        pSetValue = (IntPtr)((UInt64)pApiSetNamespace + (UInt64)SetEntry.ValueOffset);
                }

                //Get the host DLL's name from the entry
                SetValue = (Data.PE.ApiSetValueEntry)Marshal.PtrToStructure(pSetValue, typeof(Data.PE.ApiSetValueEntry));
                string ApiSetValue = string.Empty;
                if (SetValue.ValueCount != 0)
                {
                    IntPtr pValue = (IntPtr)((UInt64)pApiSetNamespace + (UInt64)SetValue.ValueOffset);
                    ApiSetValue = Marshal.PtrToStringUni(pValue, SetValue.ValueCount/2);
                }

                // Add pair to dict
                ApiSetDict.Add(ApiSetEntryKey, ApiSetValue);
            }

            // Return dict
            return ApiSetDict;
        }

        public static void CallMappedDLLModule(Data.PE.PE_META_DATA PEINFO, IntPtr ModuleMemoryBase)
        {
            IntPtr lpEntryPoint = PEINFO.Is32Bit ? (IntPtr)((UInt64)ModuleMemoryBase + PEINFO.OptHeader32.AddressOfEntryPoint) :
                                                   (IntPtr)((UInt64)ModuleMemoryBase + PEINFO.OptHeader64.AddressOfEntryPoint);
            // If there is an entry point, call it
            if (lpEntryPoint != ModuleMemoryBase)
            {
                Data.PE.DllMain fDllMain = (Data.PE.DllMain)Marshal.GetDelegateForFunctionPointer(lpEntryPoint, typeof(Data.PE.DllMain));
                try
                {
                    bool CallRes = fDllMain(ModuleMemoryBase, Data.PE.DLL_PROCESS_ATTACH, IntPtr.Zero);
                    if (!CallRes)
                    {
                        throw new InvalidOperationException("Call to entry point failed -> DLL_PROCESS_ATTACH");
                    }
                }
                catch
                {
                    throw new InvalidOperationException("Invalid entry point -> DLL_PROCESS_ATTACH");
                }
            }
        }

        public static object CallMappedDLLModuleExport(Data.PE.PE_META_DATA PEINFO, IntPtr ModuleMemoryBase, string ExportName, Type FunctionDelegateType, object[] Parameters, bool CallEntry = true)
        {
            // Call entry point if user has specified
            if (CallEntry)
            {
                CallMappedDLLModule(PEINFO, ModuleMemoryBase);
            }

            // Get export pointer
            IntPtr pFunc = GetExportAddress(ModuleMemoryBase, ExportName);

            // Call export
            return DynamicFunctionInvoke(pFunc, FunctionDelegateType, ref Parameters);
        }
    }
}