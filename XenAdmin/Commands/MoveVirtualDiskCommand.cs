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
using System.Text;
using XenAPI;
using XenAdmin.Dialogs;


namespace XenAdmin.Commands
{
    /// <summary>
    /// Shows the move virtual disk dialog
    /// </summary>
    internal class MoveVirtualDiskCommand : Command
    {
        /// <summary>
        /// Initializes a new instance of this Command. The parameter-less constructor is required in the derived
        /// class if it is to be attached to a ToolStrip menu item or button. It should not be used in any other scenario.
        /// </summary>
        public MoveVirtualDiskCommand()
        {
        }

        public MoveVirtualDiskCommand(IMainWindow mainWindow, IEnumerable<SelectedItem> selection)
            : base(mainWindow, selection)
        {
        }

        public MoveVirtualDiskCommand(IMainWindow mainWindow, VDI vdi)
            : base(mainWindow, vdi)
        {
        }

        public override string ContextMenuText
        {
            get
            {
                return Messages.MOVE_VDI_CONTEXT_MENU;
            }
        }

        protected override void ExecuteCore(SelectedItemCollection selection)
        {
            VDI vdi = (VDI)selection[0].XenObject;

            
            MainWindowCommandInterface.ShowPerXenModelObjectWizard(vdi, new MoveVirtualDiskDialog(vdi));
        }

        protected override bool CanExecuteCore(SelectedItemCollection selection)
        {
            if (selection.Count == 1)
            {
                VDI vdi = selection[0].XenObject as VDI;
                if (vdi == null)
                    return false;
                if (vdi.is_a_snapshot)
                    return false;
                if (vdi.Locked)
                    return false;
                if (vdi.VBDs.Count != 0)
                    return false;
                if (vdi.IsHaType)
                    return false;
                if (vdi.Connection.Resolve(vdi.SR).HBALunPerVDI)
                    return false;
                return true;
            }
            return false;
        }

        protected override string GetCantExecuteReasonCore(SelectedItem item)
        {
            VDI vdi = item.XenObject as VDI;
            if (vdi == null)
                return base.GetCantExecuteReasonCore(item);
            if (vdi.is_a_snapshot)
                return Messages.CANNOT_MOVE_VDI_IS_SNAPSHOT;
            if (vdi.Locked)
                return Messages.CANNOT_MOVE_VDI_IN_USE;
            if (vdi.IsHaType)
                return Messages.CANNOT_MOVE_HA_VD;
            if (vdi.IsMetadataForDR)
                return Messages.CANNOT_MOVE_DR_VD;
            if (vdi.VBDs.Count != 0)
                return Messages.CANNOT_MOVE_VDI_WITH_VBDS;
            if (vdi.Connection.Resolve(vdi.SR).HBALunPerVDI)
                return Messages.UNSUPPORTED_SR_TYPE;

            return base.GetCantExecuteReasonCore(item);
        }
    }
}

