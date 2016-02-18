﻿// <copyright file="Application.xaml.cs" company="AAllard">License: http://www.gnu.org/licenses/gpl.html GPL version 3.</copyright>

/*  File Converter - This program allow you to convert file format to another.
    Copyright (C) 2015 Adrien Allard
    email: adrien.allard.pro@gmail.com

    This program is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or any later version.

    This program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU General Public License for more details.

    You should have received a copy of the GNU General Public License along with this program.  If not, see <http://www.gnu.org/licenses/>.
 */

namespace FileConverter
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Windows;

    using FileConverter.ConversionJobs;
    using FileConverter.Diagnostics;
    using FileConverter.Windows;

    public partial class Application : System.Windows.Application
    {
        private static readonly Version Version = new Version()
                                            {
                                                Major = 0, 
                                                Minor = 6,
                                                Patch = 0,
                                            };

        private readonly List<ConversionJob> conversionJobs = new List<ConversionJob>();

        private int numberOfConversionThread = 1;

        private bool needToRunConversionThread;
        private bool cancelAutoExit;
        private UpgradeVersionDescription upgradeVersionDescription = null;

        public Application()
        {
            this.ConvertionJobs = this.conversionJobs.AsReadOnly();
        }

        public static Version ApplicationVersion
        {
            get
            {
                return Application.Version;
            }
        }

        public ReadOnlyCollection<ConversionJob> ConvertionJobs
        {
            get;
            private set;
        }

        public Settings Settings
        {
            get;
            set;
        }

        public bool ShowSettings
        {
            get;
            set;
        }

        public bool HideMainWindow
        {
            get;
            set;
        }

        public bool Verbose
        {
            get;
            set;
        }

        public void CancelAutoExit()
        {
            this.cancelAutoExit = true;
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            this.Initialize();

            if (this.needToRunConversionThread)
            {
                Thread fileConvertionThread = new Thread(this.ConvertFiles);
                fileConvertionThread.Start();
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            base.OnExit(e);

            Debug.Log("Exit application.");

            if (this.upgradeVersionDescription != null && this.upgradeVersionDescription.NeedToUpgrade)
            {
                Debug.Log("A new version of file converter has been found: {0}.", this.upgradeVersionDescription.LatestVersion);

                if (string.IsNullOrEmpty(this.upgradeVersionDescription.InstallerPath))
                {
                    Debug.LogError("Invalid installer path.");
                }
                else
                {
                    Debug.Log("Wait for the end of the installer download.");
                    while (this.upgradeVersionDescription.InstallerDownloadInProgress)
                    {
                        Thread.Sleep(1000);
                    }

                    string installerPath = this.upgradeVersionDescription.InstallerPath;
                    if (!System.IO.File.Exists(installerPath))
                    {
                        Debug.LogError("Can't find upgrade installer ({0}). Try to restart the application.", installerPath);
                        return;
                    }

                    // Start process.
                    Debug.Log("Start file converter upgrade from version {0} to {1}.", ApplicationVersion, this.upgradeVersionDescription.LatestVersion);

                    System.Diagnostics.ProcessStartInfo startInfo = new System.Diagnostics.ProcessStartInfo(installerPath)
                        {
                            UseShellExecute = true,
                        };

                    Debug.Log("Start upgrade process: {0}{1}.", System.IO.Path.GetFileName(startInfo.FileName), startInfo.Arguments);
                    System.Diagnostics.Process process = new System.Diagnostics.Process
                    {
                        StartInfo = startInfo
                    };

                    process.Start();
                }
            }

            Debug.Release();
        }

        private void Initialize()
        {
            Diagnostics.Debug.Log("File Converter v" + ApplicationVersion.ToString());
            Diagnostics.Debug.Log("The number of processors on this computer is {0}. Set the default number of conversion threads to {0}", Environment.ProcessorCount);
            this.numberOfConversionThread = Environment.ProcessorCount;
            
            // Retrieve arguments.
            Debug.Log("Retrieve arguments...");
            string[] args = Environment.GetCommandLineArgs();

#if (DEBUG)
            {
                ////System.Array.Resize(ref args, 5);
                ////args[1] = "--conversion-preset";
                ////args[2] = "To Png";
                ////args[3] = "--verbose";
                ////args[4] = @"D:\Test\images\Mario Big.png";
            }
#endif

            // Log arguments.
            for (int index = 0; index < args.Length; index++)
            {
                string argument = args[index];
                Debug.Log("Arg{0}: {1}", index, argument);
            }

            Debug.Log(string.Empty);

            if (args.Length == 1)
            {
                // Diplay help windows to explain that this application is a context menu extension.
                ApplicationStartHelp applicationStartHelp = new ApplicationStartHelp();
                applicationStartHelp.Show();
                this.HideMainWindow = true;
                return;
            }

            // Parse arguments.
            List<string> filePaths = new List<string>();
            string conversionPresetName = null;
            for (int index = 1; index < args.Length; index++)
            {
                string argument = args[index];
                if (argument.StartsWith("--"))
                {
                    // This is an optional parameter.
                    string parameterTitle = argument.Substring(2).ToLowerInvariant();

                    switch (parameterTitle)
                    {
                        case "post-install-init":
                            Settings.PostInstallationInitialization();
                            Dispatcher.BeginInvoke((Action)(() => Application.Current.Shutdown()));
                            return;

                        case "version":
                            Console.Write(ApplicationVersion.ToString());
                            Dispatcher.BeginInvoke((Action)(() => Application.Current.Shutdown()));
                            return;

                        case "settings":
                            this.ShowSettings = true;
                            this.HideMainWindow = true;
                            break;

                        case "apply-settings":
                            Settings.ApplyTemporarySettings();
                            Dispatcher.BeginInvoke((Action)(() => Application.Current.Shutdown()));
                            return;

                        case "conversion-preset":
                            if (index >= args.Length - 1)
                            {
                                Debug.LogError("Invalid format. (code 0x01)");
                                Dispatcher.BeginInvoke((Action)(() => Application.Current.Shutdown()));
                                return;
                            }

                            conversionPresetName = args[index + 1];
                            index++;
                            continue;

                        case "verbose":
                            {
                                this.Verbose = true;
                            }

                            break;

                        default:
                            Debug.LogError("Unknown application argument: '--{0}'.", parameterTitle);
                            return;
                    }
                }
                else
                {
                    filePaths.Add(argument);
                }
            }

            // Load settigns.
            Debug.Log("Load settings...");
            this.Settings = Settings.Load();
            if (this.Settings == null)
            {
                Diagnostics.Debug.LogError("The application will now shutdown. If you want to fix the problem yourself please edit or delete the file: C:\\Users\\UserName\\AppData\\Local\\FileConverter\\Settings.user.xml.");
                Dispatcher.BeginInvoke((Action)(() => Application.Current.Shutdown()));
                return;
            }

            // Check upgrade.
            if (this.Settings.CheckUpgradeAtStartup)
            {
                long fileTime = Registry.GetValue<long>(Registry.Keys.LastUpdateCheckDate);
                DateTime lastUpdateDateTime = DateTime.FromFileTime(fileTime);

                TimeSpan durationSinceLastUpdate = DateTime.Now.Subtract(lastUpdateDateTime);
                if (durationSinceLastUpdate > new TimeSpan(1, 0, 0, 0))
                {
                    Task<UpgradeVersionDescription> task = Upgrade.Helpers.GetLatestVersionDescriptionAsync(this.OnGetLatestVersionDescription);
                }
            }

            ConversionPreset conversionPreset = null;
            if (!string.IsNullOrEmpty(conversionPresetName))
            {
                conversionPreset = this.Settings.GetPresetFromName(conversionPresetName);
                if (conversionPreset == null)
                {
                    Debug.LogError("Invalid conversion preset '{0}'. (code 0x02)", conversionPresetName);
                    Dispatcher.BeginInvoke((Action)(() => Application.Current.Shutdown()));
                    return;
                }
            }

            if (conversionPreset != null)
            {
                // Create convertion jobs.
                Debug.Log("Create jobs for conversion preset: '{0}'", conversionPreset.Name);
                try
                {
                    for (int index = 0; index < filePaths.Count; index++)
                    {
                        string inputFilePath = filePaths[index];
                        ConversionJob conversionJob = ConversionJobFactory.Create(conversionPreset, inputFilePath);
                        conversionJob.PrepareConversion(inputFilePath);

                        this.conversionJobs.Add(conversionJob);
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogError(exception.Message);
                    throw;
                }

                this.needToRunConversionThread = true;
            }
        }

        private void ConvertFiles()
        {
            Thread[] jobThreads = new Thread[this.numberOfConversionThread];
            
            while (true)
            {
                // Compute conversion flags.
                ConversionFlags conversionFlags = ConversionFlags.None;
                bool allJobAreFinished = true;
                for (int jobIndex = 0; jobIndex < this.conversionJobs.Count; jobIndex++)
                {
                    ConversionJob conversionJob = this.conversionJobs[jobIndex];
                    allJobAreFinished &= !(conversionJob.State == ConversionJob.ConversionState.Ready ||
                                         conversionJob.State == ConversionJob.ConversionState.InProgress);

                    if (conversionJob.State == ConversionJob.ConversionState.InProgress)
                    {
                        conversionFlags |= conversionJob.StateFlags;
                    }
                }

                if (allJobAreFinished)
                {
                    break;
                }

                // Start job if possible.
                for (int jobIndex = 0; jobIndex < this.conversionJobs.Count; jobIndex++)
                {
                    ConversionJob conversionJob = this.conversionJobs[jobIndex];
                    if (conversionJob.State == ConversionJob.ConversionState.Ready &&
                        conversionJob.CanStartConversion(conversionFlags))
                    {
                        // Find a thread to execute the job.
                        Thread jobThread = null;
                        for (int threadIndex = 0; threadIndex < jobThreads.Length; threadIndex++)
                        {
                            Thread thread = jobThreads[threadIndex];
                            if (thread == null || !thread.IsAlive)
                            {
                                jobThread = new Thread(this.ExecuteConversionJob);
                                jobThreads[threadIndex] = jobThread;
                                break;
                            }
                        }

                        if (jobThread != null)
                        {
                            jobThread.Start(conversionJob);
                        }

                        break;
                    }
                }

                Thread.Sleep(50);
            }

            if (this.Settings.ExitApplicationWhenConversionsFinished)
            {
                bool allConversionsSucceed = true;
                for (int index = 0; index < this.conversionJobs.Count; index++)
                {
                    allConversionsSucceed &= this.conversionJobs[index].State == ConversionJob.ConversionState.Done;
                }

                if (allConversionsSucceed)
                {
                    System.Threading.Thread.Sleep((int)this.Settings.DurationBetweenEndOfConversionsAndApplicationExit * 1000);

                    if (this.cancelAutoExit)
                    {
                        return;
                    }

                    Dispatcher.BeginInvoke((Action)(() => Application.Current.Shutdown()));
                }
            }
        }

        private void ExecuteConversionJob(object parameter)
        {
            ConversionJob conversionJob = parameter as ConversionJob;
            if (conversionJob == null)
            {
                throw new System.ArgumentException("The parameter must be a conversion job.", "parameter");
            }

            if (conversionJob.State != ConversionJob.ConversionState.Ready)
            {
                Debug.LogError("Fail to execute conversion job.");
                return;
            }

            try
            {
                conversionJob.StartConvertion();
            }
            catch (Exception)
            {
                throw;
            }

            if (conversionJob.State == ConversionJob.ConversionState.Done && !System.IO.File.Exists(conversionJob.OutputFilePath))
            {
                Debug.LogError("Can't find the output file.");
            }
            else if (conversionJob.State == ConversionJob.ConversionState.Failed && System.IO.File.Exists(conversionJob.OutputFilePath))
            {
                Debug.Log("The conversion job failed but there is an output file that does exists.");
            }
        }

        private void OnGetLatestVersionDescription(UpgradeVersionDescription upgradeVersionDescription)
        {
            if (upgradeVersionDescription == null)
            {
                return;
            }
            
            Registry.SetValue(Registry.Keys.LastUpdateCheckDate, DateTime.Now.ToFileTime());

            if (upgradeVersionDescription.LatestVersion <= ApplicationVersion)
            {
                return;
            }

            this.upgradeVersionDescription = upgradeVersionDescription;
            (this.MainWindow as MainWindow).OnNewVersionReleased(upgradeVersionDescription);
        }
    }
}
