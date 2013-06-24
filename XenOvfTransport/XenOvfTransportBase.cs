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
using System.Globalization;
using System.Net;
using System.Net.Security;
using System.Reflection;
using System.Resources;
using System.Text;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Authentication;

using XenAPI;

using XenOvf.Utilities;


namespace XenOvfTransport
{
    /// <summary>
    /// 
    /// </summary>
    public class XenOvfTransportBase
    {
		public Action<XenOvfTranportEventArgs> UpdateHandler { get; set; }

		protected string m_networkUuid;
		protected bool m_isTvmIpStatic;
		protected string m_tvmIpAddress;
		protected string m_tvmSubnetMask;
		protected string m_tvmGateway;

		protected void OnUpdate(XenOvfTranportEventArgs e)
		{
			if (UpdateHandler != null)
				UpdateHandler.Invoke(e);
		}

        internal XenAPI.Session _XenSession = null;
        internal Uri _XenServer = null;
        internal string _UserName = null;
        internal string _Password = null;
        internal bool _OwnSession = false;

		protected iSCSI m_iscsi;
		private bool m_cancel;

        /// <summary>
        /// 
        /// </summary>
        public XenOvfTransportBase()
        {
			ServicePointManager.ServerCertificateValidationCallback = ValidateServerCertificate;
        }

        /// <summary>
        /// 
        /// </summary>
		public XenOvfTransportBase(Uri xenserver, Session session)
			: this()
        {
        	_XenServer = xenserver;
        	_XenSession = session;
        	_OwnSession = false;
        }

    	/// <summary>
        /// 
        /// </summary>
        ~XenOvfTransportBase()
        {
            if (_OwnSession && _XenSession != null)
            {
                _XenSession.logout();
                _OwnSession = false;
                _XenSession = null;
            }
        }

		public bool Cancel
		{
			protected get { return m_cancel; }
			set
			{
				m_cancel = value;
				if (m_iscsi != null)
					m_iscsi.Cancel = value;
			}
		}

		public void SetTvmNetwork(string networkUuid, bool isTvmIpStatic, string tvmIpAddress, string tvmSubnetMask, string tvmGateway)
		{
			m_networkUuid = networkUuid;
			m_isTvmIpStatic = isTvmIpStatic;
			m_tvmIpAddress = tvmIpAddress;
			m_tvmSubnetMask = tvmSubnetMask;
			m_tvmGateway = tvmGateway;
		}

        /// <summary>
        /// 
        /// </summary>
        public Session XenSession
        {
            get
            {
                return _XenSession;
            }
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="cert"></param>
        /// <param name="chain"></param>
        /// <param name="sslPolicyErrors"></param>
        /// <returns></returns>
        public static bool ValidateServerCertificate(object sender, X509Certificate cert, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            return true;
        }

        public static VM FindiSCSI(XenAPI.Session xenSession)
        {
            Dictionary<XenRef<VM>,VM> iSCSIDict = VM.get_all_records(xenSession);

            foreach (XenRef<VM> key in iSCSIDict.Keys)
            {
                if (iSCSIDict[key].is_a_template)
                {
                    if (iSCSIDict[key].other_config.ContainsKey("transfervm") &&
                        iSCSIDict[key].other_config["transfervm"] == "true")
                    {
                        return iSCSIDict[key];
                    }
                }
            }
            return null;
        }
    }
}
