﻿//******************************************************************************************************
//  ServiceHost.cs - Gbtc
//
//  Copyright © 2015, Grid Protection Alliance.  All Rights Reserved.
//
//  Licensed to the Grid Protection Alliance (GPA) under one or more contributor license agreements. See
//  the NOTICE file distributed with this work for additional information regarding copyright ownership.
//  The GPA licenses this file to you under the Eclipse Public License -v 1.0 (the "License"); you may
//  not use this file except in compliance with the License. You may obtain a copy of the License at:
//
//      http://www.opensource.org/licenses/eclipse-1.0.php
//
//  Unless agreed to in writing, the subject software distributed under the License is distributed on an
//  "AS-IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. Refer to the
//  License for the specific language governing permissions and limitations.
//
//  Code Modification History:
//  ----------------------------------------------------------------------------------------------------
//  09/02/2009 - J. Ritchie Carroll
//       Generated original version of source code.
//
//******************************************************************************************************

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Principal;
using System.Threading;
using GSF;
using GSF.ComponentModel;
using GSF.Configuration;
using GSF.IO;
using GSF.Reflection;
using GSF.Security;
using GSF.Security.Model;
using GSF.ServiceProcess;
using GSF.TimeSeries;
using GSF.Web.Hosting;
using GSF.Web.Model;
using GSF.Web.Model.Handlers;
using GSF.Web.Security;
using Microsoft.Owin.Hosting;
using openMIC.Model;

namespace openMIC
{
    public class ServiceHost : ServiceHostBase
    {
        #region [ Members ]

        // Events

        /// <summary>
        /// Raised when there is a new status message reported to service.
        /// </summary>
        public event EventHandler<EventArgs<Guid, string, UpdateType>> UpdatedStatus;

        /// <summary>
        /// Raised when there is a new exception logged to service.
        /// </summary>
        public event EventHandler<EventArgs<Exception>> LoggedException;

        // Fields
        private IDisposable m_webAppHost;
        private Thread m_startEngineThread;
        private bool m_serviceStopping;
        private bool m_disposed;

        #endregion

        #region [ Constructors ]

        /// <summary>
        /// Creates a new service host for openMIC application.
        /// </summary>
        public ServiceHost()
        {
            ServiceName = "openMIC";
        }

        #endregion

        #region [ Properties ]

        /// <summary>
        /// Gets the configured default web page for the application.
        /// </summary>
        public string DefaultWebPage
        {
            get;
            private set;
        }

        /// <summary>
        /// Gets the model used for the application.
        /// </summary>
        public AppModel Model
        {
            get;
            private set;
        }

        /// <summary>
        /// Gets the list of downloaders that were loaded into the input adapter collection.
        /// </summary>
        public List<Downloader> Downloaders
        {
            get
            {
                lock (InputAdapters)
                {
                    return InputAdapters.OfType<Downloader>().ToList();
                }
            }
        }

        /// <summary>
        /// Gets current performance statistics.
        /// </summary>
        public string PerformanceStatistics => ServiceHelper?.PerformanceMonitor?.Status;

        #endregion

        #region [ Methods ]

        /// <summary>
        /// Releases the unmanaged resources used by the <see cref="ServiceHost"/> object and optionally releases the managed resources.
        /// </summary>
        /// <param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
        protected override void Dispose(bool disposing)
        {
            if (!m_disposed)
            {
                try
                {
                    if (disposing)
                    {
                        m_webAppHost?.Dispose();
                    }
                }
                finally
                {
                    m_disposed = true;          // Prevent duplicate dispose.
                    base.Dispose(disposing);    // Call base class Dispose().
                }
            }
        }

        protected override void ServiceStartingHandler(object sender, EventArgs<string[]> e)
        {
            // Handle base class service starting procedures
            base.ServiceStartingHandler(sender, e);

            // Make sure openMIC specific default service settings exist
            CategorizedSettingsElementCollection systemSettings = ConfigurationFile.Current.Settings["systemSettings"];
            CategorizedSettingsElementCollection securityProvider = ConfigurationFile.Current.Settings["securityProvider"];

            systemSettings.Add("CompanyName", "Grid Protection Alliance", "The name of the company who owns this instance of the openMIC.");
            systemSettings.Add("CompanyAcronym", "GPA", "The acronym representing the company who owns this instance of the openMIC.");
            systemSettings.Add("WebHostURL", "http://localhost:8080", "The web hosting URL for remote system management.");
            systemSettings.Add("SubscriptionConnectionString", "server=localhost:6195; interface=0.0.0.0", "Connection string for data subscriptions to openMIC server.");
            systemSettings.Add("DateFormat", "MM/dd/yyyy", "The default date format to use when rendering timestamps.");
            systemSettings.Add("TimeFormat", "HH:mm.ss.fff", "The default time format to use when rendering timestamps.");
            systemSettings.Add("BootstrapTheme", "Content/bootstrap.min.css", "Path to Bootstrap CSS to use for rendering styles.");
            systemSettings.Add("DefaultDialUpRetries", 3, "Default dial-up connection retries.");
            systemSettings.Add("DefaultDialUpTimeout", 90, "Default dial-up connection timeout.");
            systemSettings.Add("DefaultFTPUserName", "anonymous", "Default FTP user name to use for device connections.");
            systemSettings.Add("DefaultFTPPassword", "anonymous", "Default FTP password to use for device connections.");
            systemSettings.Add("DefaultRemotePath", "/", "Default remote FTP path to use for device connections.");
            systemSettings.Add("DefaultLocalPath", "", "Default local path to use for file downloads.");
            systemSettings.Add("MaxRemoteFileAge", "30", "Maximum remote file age, in days, to apply for downloads when limit is enabled.");
            systemSettings.Add("MaxLocalFileAge", "365", "Maximum local file age, in days, to apply for downloads when limit is enabled.");
            systemSettings.Add("SmtpServer", "localhost", "The SMTP relay server from which to send e-mails.");
            systemSettings.Add("FromAddress", "openmic@gridprotectionalliance.org", "The from address for e-mails.");
            systemSettings.Add("SmtpUserName", "", "Username to authenticate to the SMTP server, if any.");
            systemSettings.Add("SmtpPassword", "", "Password to authenticate to the SMTP server, if any.");
            systemSettings.Add("WebRootPath", "wwwroot", "The root path for the hosted web server files. Location will be relative to install folder if full path is not specified.");
            DefaultWebPage = systemSettings["DefaultWebPage"].Value;

            Model = new AppModel();
            Model.Global.CompanyName = systemSettings["CompanyName"].Value;
            Model.Global.CompanyAcronym = systemSettings["CompanyAcronym"].Value;
            Model.Global.NodeID = Guid.Parse(systemSettings["NodeID"].Value);
            Model.Global.SubscriptionConnectionString = systemSettings["SubscriptionConnectionString"].Value;
            Model.Global.ApplicationName = "openMIC";
            Model.Global.ApplicationDescription = "open Meter Information Collection System";
            Model.Global.ApplicationKeywords = "open source, utility, software, meter, interrogation";
            Model.Global.DateFormat = systemSettings["DateFormat"].Value;
            Model.Global.TimeFormat = systemSettings["TimeFormat"].Value;
            Model.Global.DateTimeFormat = $"{Model.Global.DateFormat} {Model.Global.TimeFormat}";
            Model.Global.PasswordRequirementsRegex = securityProvider["PasswordRequirementsRegex"].Value;
            Model.Global.PasswordRequirementsError = securityProvider["PasswordRequirementsError"].Value;
            Model.Global.BootstrapTheme = systemSettings["BootstrapTheme"].Value;
            Model.Global.PasswordRequirementsRegex = securityProvider["PasswordRequirementsRegex"].Value;
            Model.Global.PasswordRequirementsError = securityProvider["PasswordRequirementsError"].Value;
            Model.Global.DefaultDialUpRetries = int.Parse(systemSettings["DefaultDialUpRetries"].Value);
            Model.Global.DefaultDialUpTimeout = int.Parse(systemSettings["DefaultDialUpTimeout"].Value);
            Model.Global.DefaultFTPUserName = systemSettings["DefaultFTPUserName"].Value;
            Model.Global.DefaultFTPPassword = systemSettings["DefaultFTPPassword"].Value;
            Model.Global.DefaultRemotePath = systemSettings["DefaultRemotePath"].Value;
            Model.Global.DefaultLocalPath = FilePath.GetAbsolutePath(systemSettings["DefaultLocalPath"].Value);
            Model.Global.MaxRemoteFileAge = int.Parse(systemSettings["MaxRemoteFileAge"].Value);
            Model.Global.MaxLocalFileAge = int.Parse(systemSettings["MaxLocalFileAge"].Value);
            Model.Global.DefaultAppPath = FilePath.GetAbsolutePath("");
            Model.Global.SmtpServer = systemSettings["SmtpServer"].Value;
            Model.Global.FromAddress = systemSettings["FromAddress"].Value;
            Model.Global.SmtpUserName = systemSettings["SmtpUserName"].Value;
            Model.Global.SmtpPassword = systemSettings["SmtpPassword"].Value;
            Model.Global.WebRootPath = systemSettings["WebRootPath"].Value;

            // Register a symbolic reference to global settings for use by default value expressions
            ValueExpressionParser.DefaultTypeRegistry.RegisterSymbol("Global", Program.Host.Model.Global);

            ServiceHelper.UpdatedStatus += UpdatedStatusHandler;
            ServiceHelper.LoggedException += LoggedExceptionHandler;

            m_startEngineThread = new Thread(() =>
            {
                const int RetryDelay = 1000;
                const int SleepTime = 200;
                const int LoopCount = RetryDelay / SleepTime;

                bool webUIStarted = false;

                while (true)
                {
                    webUIStarted = webUIStarted || TryStartWebUI();

                    if (webUIStarted)
                        break;

                    for (int i = 0; i < LoopCount; i++)
                    {
                        if (m_serviceStopping)
                            return;

                        Thread.Sleep(SleepTime);
                    }
                }
            });

            m_startEngineThread.Start();
        }

        private bool TryStartWebUI()
        {
            try
            {
                CategorizedSettingsElementCollection systemSettings = ConfigurationFile.Current.Settings["systemSettings"];

                // Attach to default web server events
                WebServer webServer = WebServer.Default;
                webServer.StatusMessage += WebServer_StatusMessage;
                webServer.ExecutionException += LoggedExceptionHandler;

                // Define types for Razor pages - self-hosted web service does not use view controllers so
                // we must define configuration types for all paged view model based Razor views here:
                webServer.PagedViewModelTypes.TryAdd("Devices.cshtml", new Tuple<Type, Type>(typeof(Device), typeof(DataHub)));
                webServer.PagedViewModelTypes.TryAdd("Companies.cshtml", new Tuple<Type, Type>(typeof(Company), typeof(DataHub)));
                webServer.PagedViewModelTypes.TryAdd("ConnectionProfiles.cshtml", new Tuple<Type, Type>(typeof(ConnectionProfile), typeof(DataHub)));
                webServer.PagedViewModelTypes.TryAdd("ConnectionProfileTasks.cshtml", new Tuple<Type, Type>(typeof(ConnectionProfileTask), typeof(DataHub)));
                webServer.PagedViewModelTypes.TryAdd("Vendors.cshtml", new Tuple<Type, Type>(typeof(Vendor), typeof(DataHub)));
                webServer.PagedViewModelTypes.TryAdd("VendorDevices.cshtml", new Tuple<Type, Type>(typeof(VendorDevice), typeof(DataHub)));
                webServer.PagedViewModelTypes.TryAdd("Users.cshtml", new Tuple<Type, Type>(typeof(UserAccount), typeof(SecurityHub)));
                webServer.PagedViewModelTypes.TryAdd("Groups.cshtml", new Tuple<Type, Type>(typeof(SecurityGroup), typeof(SecurityHub)));
                webServer.PagedViewModelTypes.TryAdd("Settings.cshtml", new Tuple<Type, Type>(typeof(Setting), typeof(DataHub)));

                // Initiate pre-compile of base templates
                if (AssemblyInfo.EntryAssembly.Debuggable)
                {
                    RazorEngine<CSharpDebug>.Default.PreCompile(LogException);
                    RazorEngine<VisualBasicDebug>.Default.PreCompile(LogException);
                }
                else
                {
                    RazorEngine<CSharp>.Default.PreCompile(LogException);
                    RazorEngine<VisualBasic>.Default.PreCompile(LogException);
                }

                // Define exception logger for CSV downloader
                CsvDownloadHandler.LogExceptionHandler = LogException;

                // Create new web application hosting environment
                m_webAppHost = WebApp.Start<Startup>(systemSettings["WebHostURL"].Value);

                return true;
            }
            catch (Exception ex)
            {
                LogException(new InvalidOperationException($"Failed to initialize web hosting: {ex.Message}", ex));
                return false;
            }
        }

        private void WebServer_StatusMessage(object sender, EventArgs<string> e)
        {
            DisplayStatusMessage(e.Argument, UpdateType.Information);
        }

        protected override void ServiceStoppingHandler(object sender, EventArgs e)
        {
            m_serviceStopping = true;

            base.ServiceStoppingHandler(sender, e);

            ServiceHelper.UpdatedStatus -= UpdatedStatusHandler;
            ServiceHelper.LoggedException -= LoggedExceptionHandler;

            m_startEngineThread.Join();
        }

        /// <summary>
        /// Logs a status message to connected clients.
        /// </summary>
        /// <param name="message">Message to log.</param>
        /// <param name="type">Type of message to log.</param>
        public void LogStatusMessage(string message, UpdateType type = UpdateType.Information)
        {
            DisplayStatusMessage(message, type);
        }

        /// <summary>
        /// Logs an exception to the service.
        /// </summary>
        /// <param name="ex">Exception to log.</param>
        public new void LogException(Exception ex)
        {
            base.LogException(ex);
            DisplayStatusMessage($"{ex.Message}", UpdateType.Alarm);
        }

        /// <summary>
        /// Sends a command request to the service.
        /// </summary>
        /// <param name="clientID">Client ID of sender.</param>
        /// <param name="principal">The principal used for role-based security.</param>
        /// <param name="userInput">Request string.</param>
        public void SendRequest(Guid clientID, IPrincipal principal, string userInput)
        {
            ClientRequest request = ClientRequest.Parse(userInput);

            if ((object)request == null)
                return;

            if (SecurityProviderUtility.IsResourceSecurable(request.Command) && !SecurityProviderUtility.IsResourceAccessible(request.Command, principal))
            {
                ServiceHelper.UpdateStatus(clientID, UpdateType.Alarm, $"Access to \"{request.Command}\" is denied.\r\n\r\n");
                return;
            }

            ClientRequestHandler requestHandler = ServiceHelper.FindClientRequestHandler(request.Command);

            if ((object)requestHandler == null)
            {
                ServiceHelper.UpdateStatus(clientID, UpdateType.Alarm, $"Command \"{request.Command}\" is not supported.\r\n\r\n");
                return;
            }

            ClientInfo clientInfo = new ClientInfo();
            clientInfo.ClientID = clientID;
            clientInfo.SetClientUser(principal);

            ClientRequestInfo requestInfo = new ClientRequestInfo(clientInfo, request);
            requestHandler.HandlerMethod(requestInfo);
        }

        public void DisconnectClient(Guid clientID)
        {
            ServiceHelper.DisconnectClient(clientID);
        }

        private void UpdatedStatusHandler(object sender, EventArgs<Guid, string, UpdateType> e)
        {
            if ((object)UpdatedStatus != null)
                UpdatedStatus(sender, new EventArgs<Guid, string, UpdateType>(e.Argument1, e.Argument2, e.Argument3));
        }

        private void LoggedExceptionHandler(object sender, EventArgs<Exception> e)
        {
            if ((object)LoggedException != null)
                LoggedException(sender, new EventArgs<Exception>(e.Argument));
        }

        #endregion
    }
}
