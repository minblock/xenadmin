﻿/* Copyright (c) Citrix Systems Inc. 
 * All rights reserved. 
 * 
 * Redistribution and use in source and binary forms, 
 * with or without modification, are permitted provided 
 * that the following conditions are met: 
 * 
 * *   Redistributions of source code must retain the above 
 *     copyright notice, this list of conditions and the 
 *     following disclaimer. 
 * *   Redistributions in binary form must reproduce the above 
 *     copyright notice, this list of conditions and the 
 *     following disclaimer in the documentation and/or other 
 *     materials provided with the distribution. 
 * 
 * THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND 
 * CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, 
 * INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF 
 * MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE 
 * DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR 
 * CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, 
 * SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, 
 * BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR 
 * SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS 
 * INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, 
 * WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING 
 * NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE 
 * OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF 
 * SUCH DAMAGE.
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using DiscUtils;
using XenAdmin.Core;
using XenOvf;
using XenOvf.Definitions;
using XenOvf.Utilities;
using XenAPI;

namespace XenOvfTransport
{
    /// <summary>
    /// 
    /// </summary>
    public class Export : XenOvfTransportBase
    {
        private const long KB = 1024;
        private const long MB = (KB * 1024);
        private const long GB = (MB * 1024);
        private const long MEMMIN = 128 * MB;

        private List<XenRef<VDI>> _vdiRefs = new List<XenRef<VDI>>();

        /// <summary>
        /// 
        /// </summary>
        public Export()
            : base()
        {
        }
        /// <summary>
        /// 
        /// </summary>
        public Export(Uri xenserver, Session session)
            : base(xenserver, session)
        {
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="targetPath"></param>
        /// <param name="ovfname"></param>
        /// <param name="vmUuid"></param>
        /// <returns></returns>
        public EnvelopeType Process(string targetPath, string ovfname, string[] vmUuid)
        {
            return Process(_XenSession, targetPath, ovfname, vmUuid);
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="xenSession"></param>
        /// <param name="targetPath"></param>
        /// <param name="ovfname"></param>
        /// <param name="vmUuid"></param>
        /// <returns></returns>
        public EnvelopeType Process(Session xenSession, string targetPath, string ovfname, string[] vmUuid)
        {
            _XenSession = xenSession;

            List<EnvelopeType> envList = new List<EnvelopeType>();

            foreach (string vmuuid in vmUuid)
                envList.Add(_export(_XenSession, targetPath, ovfname, vmuuid));

            EnvelopeType ovfEnv = OVF.Merge(envList, ovfname);

			if (AutoSave)
            {
                string ovffilename = Path.Combine(targetPath, string.Format(@"{0}.ovf", ovfname));
                OVF.SaveAs(ovfEnv, ovffilename);
            }
        	OnUpdate(new XenOvfTranportEventArgs(XenOvfTranportEventType.ExportThreadComplete, "Export", Messages.COMPLETED_EXPORT));
            return ovfEnv;
        }

    	/// <summary>
    	/// 
    	/// </summary>
		public bool AutoSave { get; set; }

        /// <summary>
        /// 
        /// </summary>
        public bool ShouldVerifyDisks { get; set; }

    	/// <summary>
    	/// 
    	/// </summary>
		public bool MetaDataOnly { get; set; }

        private EnvelopeType _export(Session xenSession, string targetPath, string ovfname, string vmUuid)
        {
        	EnvelopeType ovfEnv;

            try
            {
                Log.Audit("Export: {0}, {1}", ovfname, targetPath);

                #region GET VM Reference
                XenRef<VM> vmRef = null;

                try
                {
                    vmRef = VM.get_by_uuid(xenSession, vmUuid);
                }
                catch
                {
                    Log.Warning("VM not found as uuid: {0}, trying as name-label", vmUuid);
                    vmRef = null;
                }
                if (vmRef == null)
                {
					try
					{
						List<XenRef<VM>> vmRefs = VM.get_by_name_label(xenSession, vmUuid);
						vmRef = vmRefs[0];
						Log.Trace("{0} VM(s) found by label {1}", vmRefs.Count, vmUuid);
						if (vmRefs.Count > 1)
						{
							Log.Warning("Only exporting FIRST VM with name {0}", vmUuid);
						}

					}
					catch
					{
						var msg = string.Format(Messages.ERROR_VM_NOT_FOUND, vmUuid);
						Log.Error(msg);
						throw new Exception(msg);
					}
                }
                #endregion

                VM vm = VM.get_record(xenSession, vmRef);

                if (vm.power_state != vm_power_state.Halted && vm.power_state != vm_power_state.Suspended)
                {
                	var message = string.Format(Messages.ERROR_VM_NOT_HALTED, vm.Name);
                	OnUpdate(new XenOvfTranportEventArgs(XenOvfTranportEventType.ExportProgress, "Export", message));
                    Log.Info(message);
                    throw new Exception(message);
                }

                #region CREATE ENVELOPE / ADD VIRTUAL SYSTEM
                ovfEnv = OVF.CreateEnvelope(ovfname);
                string vsId = OVF.AddVirtualSystem(ovfEnv, vm.name_label);
                string vhsId = OVF.AddVirtualHardwareSection(ovfEnv, vsId);
                #endregion

                #region TRY TO ID OS
                XenRef<VM_guest_metrics> vmgmRef = VM.get_guest_metrics(xenSession, vmRef);
				if (!vmgmRef.opaque_ref.ToUpper().Contains("NULL"))
                {
                    VM_guest_metrics vmgm = VM_guest_metrics.get_record(xenSession, vmgmRef);
                    //VM_metrics vmm = VM_metrics.get_record(xenSession, VM.get_metrics(xenSession, vmRef));
                    if (vmgm.os_version != null && vmgm.os_version.Count > 0)
                    {
                        foreach (string key in vmgm.os_version.Keys)
                        {
                            if (key.ToLower().Equals("name"))
                            {
                                ushort osid = ValueMaps.OperatingSystem(vmgm.os_version[key]);
                                if (osid == 0xFFFF) { osid = 1; } // change to OTHER since search failed.
                                string version = OVF.GetContentMessage("SECTION_OPERATINGSYSTEM_INFO");
                                if (vmgm.os_version.ContainsKey("major") &&
                                    vmgm.os_version.ContainsKey("minor"))
                                {
                                    version = string.Format(OVF.GetContentMessage("SECTION_OPERATINGSYSTEM_VERSION"), vmgm.os_version["major"], vmgm.os_version["minor"]);
                                }
                                string osname = (vmgm.os_version[key].Split(new [] { '|' }))[0];
                                OVF.UpdateOperatingSystemSection(ovfEnv, vsId, osname, version, osid);
                                break;
                            }
                        }
                    }
                }
                #endregion

                #region ADD VSSD
                // IS PV'd? for VirtualSystemType identification.
                string typeformat = @"{0}-{1}-{2}";
                string vmtype = string.Format(typeformat, "hvm", "3.0", "unknown");
                if (vm.HVM_boot_policy != null && vm.HVM_boot_policy == Properties.Settings.Default.xenBootOptions)
                {
                    if (vm.domarch != null && vm.domarch.Length > 0)
                    {
                        vmtype = string.Format(typeformat, vm.domarch, "3.0", "unknown");
                    }
                }
                else
                {
                    if (vm.domarch != null && vm.domarch.Length > 0)
                    {
                        vmtype = string.Format(typeformat, "xen", "3.0", vm.domarch);
                    }
                    else
                    {
                        vmtype = string.Format(typeformat, "xen", "3.0", "unknown");
                    }
                }
                OVF.AddVirtualSystemSettingData(ovfEnv, vsId, vhsId, vm.name_label, OVF.GetContentMessage("VSSD_CAPTION"), vm.name_description, Guid.NewGuid().ToString(), vmtype);
                #endregion

                #region ADD CPUS
                OVF.SetCPUs(ovfEnv, vsId, (ulong)vm.VCPUs_max);
                #endregion

                #region ADD MEMORY
                OVF.SetMemory(ovfEnv, vsId, (ulong)(vm.memory_dynamic_max / MB), "MB");
                #endregion

                #region ADD NETWORKS
                List<XenRef<VIF>> vifs = VM.get_VIFs(xenSession, vmRef);
                foreach (XenRef<VIF> vifref in vifs)
                {
                    VIF vif = VIF.get_record(xenSession, vifref);
                    XenRef<Network> netRef = vif.network;
                    Network net = Network.get_record(xenSession, netRef);

                    // Why is the following call reference using name_label where other references use uuid?
                    OVF.AddNetwork(ovfEnv, vsId, net.uuid, net.name_label, net.name_description, vif.MAC);
                }
                #endregion

				#region SET STARTUP OPTIONS
				if (Helpers.BostonOrGreater(xenSession.Connection))
					OVF.AddStartupSection(ovfEnv, true, vsId, vm.order, vm.start_delay, vm.shutdown_delay);
				#endregion

				#region GET AND EXPORT DISKS using iSCSI
				List<XenRef<VBD>> vbdlist = VM.get_VBDs(xenSession, vmRef);
                _vdiRefs.Clear();

                int diskIndex = 0;

                foreach (XenRef<VBD> vbdref in vbdlist)
                {
                    VBD vbd = VBD.get_record(xenSession, vbdref);
                    XenRef<VDI> vdi = null;
                    Stream source = null;
                    FileStream fs = null;
                    if (vbd.type == vbd_type.CD)
                    {
                        string rasdid = OVF.AddCDROM(ovfEnv, vsId, vbd.uuid, OVF.GetContentMessage("RASD_16_CAPTION"), OVF.GetContentMessage("RASD_16_DESCRIPTION"));
                        OVF.SetTargetDeviceInRASD(ovfEnv, vsId, rasdid, vbd.userdevice);
                    }
                    else
                    {
                        try
                        {
                            vdi = VBD.get_VDI(xenSession, vbdref);
                            if (vdi != null && !string.IsNullOrEmpty(vdi.opaque_ref) && !(vdi.opaque_ref.ToLower().Contains("null")))
                            {
                                _vdiRefs.Add(vdi);
                                VDI lVdi = VDI.get_record(xenSession, vdi);
                                string destinationFilename = Path.Combine(targetPath, string.Format(@"{0}.vhd", lVdi.uuid));
                                string diskid = Guid.NewGuid().ToString();

                                string diskName = lVdi.name_label;

                                if (diskName == null)
                                    diskName = string.Format("{0} {1}", OVF.GetContentMessage("RASD_19_CAPTION"), diskIndex);

                                OVF.AddDisk(ovfEnv, vsId, diskid, Path.GetFileName(destinationFilename), vbd.bootable, diskName, lVdi.name_description, (ulong)lVdi.physical_utilisation, (ulong)lVdi.virtual_size);
                                OVF.SetTargetDeviceInRASD(ovfEnv, vsId, diskid, vbd.userdevice);

                                diskIndex++;
                            }
                        }
                        catch (Exception ex)
                        {
							Log.Info("Export: VDI Skipped: {0}: {1}", vdi.opaque_ref, ex.Message);
                        }
                        finally
                        {
                            if (source != null) source.Close();
                            if (fs != null) fs.Close();
                        }
                    }
                }
                #endregion

				if (!MetaDataOnly)
                {
                    _copydisks(ovfEnv, ovfname, targetPath);
                }

                #region ADD XEN SPECIFICS
                if (vm.HVM_boot_params != null)
                {
                    Dictionary<string, string> _params = vm.HVM_boot_params;
                    foreach (string key in _params.Keys)
                    {
                        if (key.ToLower().Equals("order"))
                        {
                            OVF.AddOtherSystemSettingData(ovfEnv, vsId, "HVM_boot_params", _params[key], OVF.GetContentMessage("OTHER_SYSTEM_SETTING_DESCRIPTION_1"));
                        }
                    }
                }
                if (vm.HVM_boot_policy != null && vm.HVM_boot_policy.Length > 0)
                {
                    OVF.AddOtherSystemSettingData(ovfEnv, vsId, "HVM_boot_policy", vm.HVM_boot_policy, OVF.GetContentMessage("OTHER_SYSTEM_SETTING_DESCRIPTION_2"));
                }
                if (vm.HVM_shadow_multiplier != 1.0)
                {
					OVF.AddOtherSystemSettingData(ovfEnv, vsId, "HVM_shadow_multiplier", Convert.ToString(vm.HVM_shadow_multiplier), OVF.GetContentMessage("OTHER_SYSTEM_SETTING_DESCRIPTION_1"));
                }
                if (vm.platform != null)
                {
                    Dictionary<string, string> platform = vm.platform;
                    StringBuilder sb = new StringBuilder();
                    foreach (string key in platform.Keys)
                    {
                        sb.AppendFormat(@"{0}={1};", key, platform[key]);
                    }
					OVF.AddOtherSystemSettingData(ovfEnv, vsId, "platform", sb.ToString(), OVF.GetContentMessage("OTHER_SYSTEM_SETTING_DESCRIPTION_3"));
                }
                if (vm.PV_args != null && vm.PV_args.Length > 0)
                {
					OVF.AddOtherSystemSettingData(ovfEnv, vsId, "PV_args", vm.PV_args, OVF.GetContentMessage("OTHER_SYSTEM_SETTING_DESCRIPTION_1"));
                }
                if (vm.PV_bootloader != null && vm.PV_bootloader.Length > 0)
                {
					OVF.AddOtherSystemSettingData(ovfEnv, vsId, "PV_bootloader", vm.PV_bootloader, OVF.GetContentMessage("OTHER_SYSTEM_SETTING_DESCRIPTION_1"));
                }
                if (vm.PV_bootloader_args != null && vm.PV_bootloader_args.Length > 0)
                {
					OVF.AddOtherSystemSettingData(ovfEnv, vsId, "PV_bootloader_args", vm.PV_bootloader_args, OVF.GetContentMessage("OTHER_SYSTEM_SETTING_DESCRIPTION_1"));
                }
                if (vm.PV_kernel != null && vm.PV_kernel.Length > 0)
                {
					OVF.AddOtherSystemSettingData(ovfEnv, vsId, "PV_kernel", vm.PV_kernel, OVF.GetContentMessage("OTHER_SYSTEM_SETTING_DESCRIPTION_1"));
                }
                if (vm.PV_legacy_args != null && vm.PV_legacy_args.Length > 0)
                {
					OVF.AddOtherSystemSettingData(ovfEnv, vsId, "PV_legacy_args", vm.PV_legacy_args, OVF.GetContentMessage("OTHER_SYSTEM_SETTING_DESCRIPTION_1"));
                }
                if (vm.PV_ramdisk != null && vm.PV_ramdisk.Length > 0)
                {
					OVF.AddOtherSystemSettingData(ovfEnv, vsId, "PV_ramdisk", vm.PV_ramdisk, OVF.GetContentMessage("OTHER_SYSTEM_SETTING_DESCRIPTION_1"));
                }
                #endregion

                OVF.FinalizeEnvelope(ovfEnv);
            }
            catch (Exception ex)
            {
				if (ex is OperationCanceledException)
					throw;
                Log.Error(Messages.ERROR_EXPORT_FAILED);
                throw new Exception(Messages.ERROR_EXPORT_FAILED, ex);
            }
            return ovfEnv;
        }
        
        private void _copydisks(EnvelopeType ovfEnv, string label, string targetPath)
        {
        	m_iscsi = new iSCSI
        	          	{
        	          		UpdateHandler = iscsi_UpdateHandler,
        	          		Cancel = Cancel//in case it has already been cancelled
        	          	};
			m_iscsi.ConfigureTvmNetwork(m_networkUuid, m_isTvmIpStatic, m_tvmIpAddress, m_tvmSubnetMask, m_tvmGateway);

            try
            {
                foreach (XenRef<VDI> vdiuuid in _vdiRefs)
                {
                    string uuid = "";
                    string destinationFilename = "";

                    try
                    {
                        uuid = VDI.get_uuid(_XenSession, vdiuuid);
                        destinationFilename = Path.Combine(targetPath, string.Format(@"{0}.vhd", uuid));
                        if (File.Exists(destinationFilename))
                        {
                            destinationFilename = Path.Combine(targetPath, string.Format(@"{0}_{1}.vhd", uuid, Thread.CurrentThread.ManagedThreadId));
                            OVF.UpdateFilename(ovfEnv, string.Format(@"{0}.vhd", uuid), string.Format(@"{0}_{1}.vhd", uuid, Thread.CurrentThread.ManagedThreadId));
                            Log.Info("{0}: VHD Name collision, renamed {0} to {1}", label, string.Format(@"{0}.vhd", uuid), string.Format(@"{0}_{1}.vhd", uuid, Thread.CurrentThread.ManagedThreadId));
                        }
                        OnUpdate(new XenOvfTranportEventArgs(XenOvfTranportEventType.MarqueeOn, "Export", string.Format(Messages.FILES_TRANSPORT_SETUP, uuid + ".vhd")));
						using (Stream source = m_iscsi.Connect(_XenSession, uuid, true))
                        {
                            OnUpdate(new XenOvfTranportEventArgs(XenOvfTranportEventType.MarqueeOff, "Export", ""));
                            using (FileStream fs = new FileStream(destinationFilename, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
                            {
                                // Create a geometry to give to DiscUtils.Vhd.Disk.InitializeDynamic() just so it doesn't override capacity
                                // when initializing the .
                                DiscUtils.Geometry geometry = DiscUtils.Geometry.FromCapacity(source.Length);

                                using (DiscUtils.Vhd.Disk destination = DiscUtils.Vhd.Disk.InitializeDynamic(fs, Ownership.None, source.Length, geometry))
                                {
									m_iscsi.Copy(source, destination.Content, Path.GetFileName(destinationFilename), ShouldVerifyDisks);
                                }
                            }
                        }

                        if (ShouldVerifyDisks)
                        {
                            using (var target = new DiscUtils.Vhd.Disk(destinationFilename, FileAccess.Read))
                            {
								m_iscsi.Verify(target.Content, destinationFilename);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
						if (ex is OperationCanceledException)
							throw;
                        var msg = string.Format(Messages.ISCSI_COPY_ERROR, destinationFilename);
                        Log.Error(msg);
                        OnUpdate(new XenOvfTranportEventArgs(XenOvfTranportEventType.Failure, "Export", msg, ex));
                        throw new Exception(msg, ex);
                    }
                    finally
                    {
                        OnUpdate(new XenOvfTranportEventArgs(XenOvfTranportEventType.MarqueeOn, "Export", string.Format(Messages.FILES_TRANSPORT_CLEANUP, uuid + ".vhd")));
						m_iscsi.Disconnect(_XenSession);
                        OnUpdate(new XenOvfTranportEventArgs(XenOvfTranportEventType.MarqueeOff, "Export", ""));
                    }
                }
            }
            finally
            {
                _vdiRefs.Clear();
            }
        }

		private void iscsi_UpdateHandler(XenOvfTranportEventArgs e)
		{
			OnUpdate(e);
		}
    }
}
