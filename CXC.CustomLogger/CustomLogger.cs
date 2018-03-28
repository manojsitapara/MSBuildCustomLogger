using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace CXC.CustomLogger
{
    public class CustomLogger : Logger
    {
        private FileInfo _readMeFile = null;
        private FileInfo _logFile = null;


        public string CopyBuildFilesToDeploymentDirTarget = string.Empty;
        public string CopyDbDeployOutputFilesToDeploymentDirTarget = string.Empty;
        public List<string> SqlFileList = new List<string>();
        public string CurrentTargetName = string.Empty;
        public List<string> ListOfBuildFiles = new List<string>();
        private static readonly string STR_START_CHANGE_SCRIPT = "-- START CHANGE SCRIPT";
        Options options = new Options();

        class Options
        {
            public string FromEmailAddress = string.Empty;
            public string ToEmailAddress = string.Empty;
        }


        void ParseParameters()
        {
            if (Parameters == null)
            {
                throw new ArgumentException("Parameters is null");
            };

            string[] parameters = Parameters.Split(';');

            foreach (string parameter in parameters)
            {
                if (parameter.ToUpper().StartsWith("FROMEMAILADDRESS"))
                {
                    int index = parameter.IndexOf('=');
                    options.FromEmailAddress = parameter.Substring(index + 1).Trim('"').Trim();
                }
                else if (parameter.ToUpper().StartsWith("TOEMAILADDRESS"))
                {
                    int index = parameter.IndexOf('=');
                    options.ToEmailAddress = parameter.Substring(index + 1).Trim('"').Trim();
                }
                else 
                {
                    throw new ArgumentException(Parameters + "UnrecognizedParameter", parameter);
                }
            }
        }

        public override void Initialize(IEventSource eventSource)
        {
            ParseParameters();
            eventSource.BuildStarted += new BuildStartedEventHandler(eventSource_BuildStarted);
            eventSource.WarningRaised += new BuildWarningEventHandler(eventSource_WarningRaised);
            eventSource.ErrorRaised += new BuildErrorEventHandler(eventSource_ErrorRaised);
            eventSource.BuildFinished += new BuildFinishedEventHandler(eventSource_BuildFinished);
            eventSource.TargetStarted += eventSource_TargetStarted;
            eventSource.TargetFinished += eventSource_TargetEnded;
            eventSource.MessageRaised += this.MessageRaised;

        }

        private void eventSource_BuildStarted(Object sender, BuildStartedEventArgs e)
        {
            _readMeFile = new FileInfo("../Log/Readme_" + DateTime.Now.ToString("MMddyy_HHmmss") + ".txt");
            _logFile = new FileInfo("../Log/MSBuild_" + DateTime.Now.ToString("MMddyy_HHmmss") + ".txt");
            if (_readMeFile.Exists)
            {
                _readMeFile.Delete();
            }

            if (_logFile.Exists)
            {
                _logFile.Delete();
            }

            using (var stream = new StreamWriter(_readMeFile.FullName, true))
            {
                stream.WriteLine("Build started. " + System.DateTime.Now.ToString("MM/dd/yyyy HH:mm:ss.fff", CultureInfo.InvariantCulture));
            }

            using (var stream = new StreamWriter(_logFile.FullName, true))
            {
                stream.WriteLine(e.Message);
            }
        }

        private void eventSource_WarningRaised(object sender, BuildWarningEventArgs e)
        {
            using (var stream = new StreamWriter(_logFile.FullName, true))
            {
                stream.WriteLine("Warning at: " + e.LineNumber + "," + e.ColumnNumber + " - " + e.Message);
            }
        }

        private void eventSource_ErrorRaised(Object sender, BuildErrorEventArgs e)
        {
            using (var stream = new StreamWriter(_logFile.FullName, true))
            {
                stream.WriteLine("Error at: " + e.LineNumber + "," + e.ColumnNumber + " - " + e.Message);
                LaunchCommandLineApp("Build Failed for Target " + CurrentTargetName ,  e.Message);
            }
        }

        private void MessageRaised(object sender, BuildMessageEventArgs e)
        {
            using (var stream = new StreamWriter(_logFile.FullName, true))
            {
                stream.WriteLine(e.Message);
            }

            if (String.Equals(CopyBuildFilesToDeploymentDirTarget, "CopyBuildFilesToDeploymentDir", StringComparison.CurrentCultureIgnoreCase))
            {

                string[] prefixes = { "Copy " };
                string message = e.Message;

                bool result = prefixes.Any(prefix => message.ToUpper().StartsWith(prefix.ToUpper()));

                if (result)
                {
                    ListOfBuildFiles.Add(message);
                }

            }

            if (String.Equals(CopyDbDeployOutputFilesToDeploymentDirTarget, "CopyDbDeployOutputFilesToDeploymentDir", StringComparison.CurrentCultureIgnoreCase))
            {
                string[] prefixes = { "Copy" };
                string message = e.Message;
                bool result = prefixes.Any(prefix => message.ToUpper().StartsWith(prefix.ToUpper()));

                if (result)
                {


                    string dbDeployCopyCommand = message;

                    //Check if file contatin dbDeploy_output_
                    if (dbDeployCopyCommand.ToUpper().Contains("dbDeploy_output_".ToUpper()))
                    {
                        //get substring after last space from message
                        int postionOfLastSpace = dbDeployCopyCommand.LastIndexOf(@" ", StringComparison.Ordinal) + 1;
                        string dbDeployFileFullPath = dbDeployCopyCommand.Substring(postionOfLastSpace,
                            dbDeployCopyCommand.Length - postionOfLastSpace);


                        var dbDeployFilePathData = File.ReadAllLines(dbDeployFileFullPath);
                        var dbDeployFileWithRequiredData =
                            dbDeployFilePathData.Where(g => g.Contains(STR_START_CHANGE_SCRIPT));
                        string[] splitedString;
                        foreach (var item in dbDeployFileWithRequiredData)
                        {
                            splitedString = item.Split(new string[] { STR_START_CHANGE_SCRIPT }, StringSplitOptions.None);
                            string initialSqlFileName = splitedString[1];
                            //1.CXCCore_SchemaChanges / 1.Ticket_29434.sql(1)
                            string sqlFileAfterRemovedLastIndex =
                                initialSqlFileName.Remove(initialSqlFileName.LastIndexOf(' '));
                            //1.CXCCore_SchemaChanges / 1.Ticket_29434.sql
                            string finalSqlFileName =
                                sqlFileAfterRemovedLastIndex.Substring(sqlFileAfterRemovedLastIndex.IndexOf(".") + 1);
                            //CXCCore_SchemaChanges / 1.Ticket_29434.sql
                            SqlFileList.Add(finalSqlFileName);
                        }
                    }
                }
            }
        }


        private void eventSource_TargetStarted(Object sender, TargetStartedEventArgs e)
        {

            if (String.Equals(e.TargetName, "CopyBuildFilesToDeploymentDir", StringComparison.CurrentCultureIgnoreCase))
            {
                CopyBuildFilesToDeploymentDirTarget = e.TargetName;
            }

            if (String.Equals(e.TargetName, "CopyDbDeployOutputFilesToDeploymentDir", StringComparison.CurrentCultureIgnoreCase))
            {
                CopyDbDeployOutputFilesToDeploymentDirTarget = e.TargetName;
            }
            CurrentTargetName = e.TargetName;


        }

        private void eventSource_TargetEnded(object sender, TargetFinishedEventArgs e)
        {

            if (String.Equals(e.TargetName, "CopyBuildFilesToDeploymentDir", StringComparison.CurrentCultureIgnoreCase))
            {
                var ordered = ListOfBuildFiles.OrderBy(p => Path.GetExtension(p));
                string previousExtension = string.Empty;
                foreach (var fileName in ordered)
                {
                    string currentExtension = Path.GetExtension(fileName);
                    if (previousExtension != currentExtension)
                    {
                        using (var stream = new StreamWriter(_readMeFile.FullName, true))
                        {
                            stream.WriteLine(Environment.NewLine);
                            stream.WriteLine("Following " + currentExtension + " has been copied");

                        }
                    }
                    //Console.WriteLine(fileName);
                    using (var stream = new StreamWriter(_readMeFile.FullName, true))
                    {
                        stream.WriteLine(fileName);
                    }
                    previousExtension = currentExtension;
                }
            }

            if (String.Equals(e.TargetName, "CopyDbDeployOutputFilesToDeploymentDir", StringComparison.CurrentCultureIgnoreCase))
            {
                SqlFileList = SqlFileList.OrderBy(i => i).ToList();

                if (SqlFileList.Count > 0)
                {
                    using (var stream = new StreamWriter(_readMeFile.FullName, true))
                    {
                        stream.WriteLine(Environment.NewLine);
                        stream.WriteLine("Following .sql has been copied");
                    }
                    
                }
                foreach (var sqlFileName in SqlFileList)
                {
                    using (var stream = new StreamWriter(_readMeFile.FullName, true))
                    {
                        stream.WriteLine(sqlFileName);

                    }
                }

            }
        }

        //triggered when the compiling process is over
        private void eventSource_BuildFinished(object sender, BuildFinishedEventArgs e)
        {
            //Console.WriteLine("Result: " + e.Message);
        }


        private void LaunchCommandLineApp(string subject, string message)
        {
            string currentDir = Path.GetDirectoryName(Environment.CurrentDirectory);

            Console.WriteLine(currentDir);
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.CreateNoWindow = true;
            startInfo.UseShellExecute = false;
            startInfo.FileName = currentDir + "\\lib\\sendemail.exe";
            startInfo.WindowStyle = ProcessWindowStyle.Hidden;
            startInfo.Arguments = "-f "+ options.FromEmailAddress + " -t "+ options.ToEmailAddress + " -u "+ subject  + " -m "+ message + " See log file for additional details...  -s SERVERNAME:PORT -xu USERNAME -xp PASSWORD -o tls=yes";

            try
            {
                using (Process exeProcess = Process.Start(startInfo))
                {
                    exeProcess.WaitForExit();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                throw ex;
            }
        }
    }
}
