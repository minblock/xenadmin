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
using System.Timers;
using XenAdmin.Actions;
using XenAdmin.Alerts;
using XenAdmin.Core;
using XenAdmin.Network;
using XenAPI;
using XenAdmin.Dialogs;


namespace XenAdmin
{
    /// <summary>
    /// A custom Timer that checks at regular intervals if any server licenses have expired. Also contains logic
    /// for testing license state on connection as to whether we should warn about a soon to expire license.
    /// </summary>
    class LicenseTimer : System.Timers.Timer
    {
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        private static readonly TimeSpan EXPIRED_REMINDER_FREQUENCY = new TimeSpan(0, 0, 30, 0); // How frequently to remind user when a license has expired
        private static readonly TimeSpan CONNECTION_WARN_THRESHOLD = new TimeSpan(29, 0, 0, 0); // When to start warning on connection 
        private static readonly TimeSpan RUNNING_WARN_FREQUENCY = new TimeSpan(1, 0, 0, 0); // How frequently to remind when XC is running

        private static DateTime lastPeriodicLicenseWarning;
        private readonly LicenseManagerLauncher licenseManagerLauncher;

        public LicenseTimer(LicenseManagerLauncher licenseManagerLauncher)
        {
            Elapsed += new ElapsedEventHandler(licenseTimerElapsed);
            AutoReset = true;
            Interval = EXPIRED_REMINDER_FREQUENCY.TotalMilliseconds;
            lastPeriodicLicenseWarning = DateTime.UtcNow;
            this.licenseManagerLauncher = licenseManagerLauncher;
            Start();
        }

        /// <summary>
        /// Call this to check the server licenses when a connection has been made or on periodic check.
        /// If a license has expired, the user is warned.
        /// The logic for the periodic license warning check: only shows the less than 30 day warnings once every day XC is running.
        /// </summary>
        /// <param name="connection">The connection to check licenses on</param>
        /// <param name="periodicCheck">Whehter it is a periodic check</param>
        internal bool CheckActiveServerLicense(IXenConnection connection, bool periodicCheck)
        {
            // don't popup the license manager dialog if host is ClearwaterOrGreater and the feature is disabled
            bool popupLicenseMgr = !(Helpers.ClearwaterOrGreater(connection) && HiddenFeatures.LicenseNagHidden);

            // If the host is Dundee or greater, then the license alerts are generated by the server, so XenCenter shouldn't create any license alerts
            bool createAlert = !Helpers.DundeeOrGreater(connection);

            if (!popupLicenseMgr && !createAlert)
                return false;

            DateTime now = DateTime.UtcNow - connection.ServerTimeOffset;
            foreach (Host host in connection.Cache.Hosts)
            {
                if (host.IsXCP)
                    continue;

                DateTime expiryDate = host.LicenseExpiryUTC;
                TimeSpan timeToExpiry = expiryDate.Subtract(now);

                if (expiryDate < now)
                {
                    // License has expired. Pop up the License Manager.
                    Program.Invoke(Program.MainWindow, () => showLicenseSummaryExpired(host, now, expiryDate, createAlert, popupLicenseMgr));
                    return true;
                }
                if (timeToExpiry < CONNECTION_WARN_THRESHOLD &&
                    (!periodicCheck || DateTime.UtcNow.Subtract(lastPeriodicLicenseWarning) > RUNNING_WARN_FREQUENCY))
                {
                    // If the license is sufficiently close to expiry date, show the warning
                    // If it's a periodic check, only warn if XC has been open for one day
                    if (periodicCheck)
                        lastPeriodicLicenseWarning = DateTime.UtcNow;
                    Program.Invoke(Program.MainWindow, () => showLicenseSummaryWarning(Helpers.GetName(host), now, expiryDate, createAlert, popupLicenseMgr));
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Check to see if any licenses have expired as the timer periodically elapses.
        /// </summary>
        private void licenseTimerElapsed(object sender, ElapsedEventArgs e)
        {
            if (licenseManagerLauncher.LicenceDialogIsShowing)
                return;
            foreach (IXenConnection xc in ConnectionsManager.XenConnectionsCopy)
            {
                if (CheckActiveServerLicense(xc, true))
                    return;
            }          
        }

        /// <summary>
        /// Shows the license summary dialog to the user as their license will soon expire.
        /// </summary>
        private void showLicenseSummaryWarning(String hostname, DateTime now, DateTime expiryDate, bool createAlert, bool popupLicenseMgr)
        {
            Program.AssertOnEventThread();

            log.InfoFormat("Server {0} is within 30 days of expiry ({1}). Show License Summary if needed",
                hostname,
                HelpersGUI.DateTimeToString(expiryDate, Messages.DATEFORMAT_DMY_HMS, true));

            if (createAlert)
            {
                var alert = new LicenseAlert(hostname, now, expiryDate) { LicenseManagerLauncher = licenseManagerLauncher };
                Alert.AddAlert(alert);
            }

            if (!popupLicenseMgr)
                return;

            if (Program.RunInAutomatedTestMode)
                log.Debug("In automated test mode: quashing license expiry warning");
            else
            {
                licenseManagerLauncher.Parent = Program.MainWindow;
                licenseManagerLauncher.LaunchIfRequired(true, ConnectionsManager.XenConnections);
            }    
        }

        /// <summary>
        /// Shows the license summary dialog to the user as their license has expired.
        /// </summary>
        /// <param name="host"></param>
        /// <param name="now"></param>
        /// <param name="expiryDate">Should be expressed in local time.</param>
        private void showLicenseSummaryExpired(Host host, DateTime now, DateTime expiryDate, bool createAlert, bool popupLicenseMgr)
        {
            Program.AssertOnEventThread();

            log.InfoFormat("Server {0} has expired ({1}). Show License Summary if needed",
                host.Name,
                HelpersGUI.DateTimeToString(expiryDate, Messages.DATEFORMAT_DMY_HMS, true));

            if (createAlert)
            {
                var alert = new LicenseAlert(host.Name, now, expiryDate) { LicenseManagerLauncher = licenseManagerLauncher };
                Alert.AddAlert(alert);
            }

            if (!popupLicenseMgr)
                return;

            if (Program.RunInAutomatedTestMode)
                log.Debug("In automated test mode: quashing license expiry warning");
            else
                licenseManagerLauncher.LaunchIfRequired(true, ConnectionsManager.XenConnections);
        }
    }
}
