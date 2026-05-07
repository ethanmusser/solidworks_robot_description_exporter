using log4net;
using log4net.Appender;
using log4net.Core;
using log4net.Layout;
using log4net.Layout.Pattern;
using log4net.Repository.Hierarchy;

using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace SW2URDF.Utilities
{
    public class FileNamePatternConverter : PatternLayoutConverter
    {
        protected override void Convert(TextWriter writer, LoggingEvent loggingEvent)
        {
            writer.Write(Path.GetFileName(loggingEvent.LocationInformation.FileName));
        }
    }

    public static class Logger
    {
        private static bool Initialized = false;

        public static void Setup()
        {
            if (Initialized)
            {
                return;
            }

            Hierarchy hierarchy = (Hierarchy)LogManager.GetRepository();

            // ConversionPattern is intentionally LOCATION-FREE. The previous
            // pattern "%date %-5level %filename: %line - %message" forced
            // log4net to call StackTrace.CaptureStackTrace per Info call -
            // hundreds of ms each under a debugger with PDBs loaded. That
            // turned hot-path logging (e.g. DrawAxisOverlay) into an
            // apparent SolidWorks hang because the call stack always
            // landed inside FileNamePatternConverter.Convert /
            // LocationInfo. The custom FileNamePatternConverter still
            // exists in this file for archaeology / reuse but is NOT
            // wired into the active layout.
            //
            // If you need filename / line for a specific debug session,
            // add them BACK temporarily and DO NOT ship the change. The
            // structural rule: location-aware log4net layouts walk the
            // managed stack once per LoggingEvent, which is fine for
            // error / batch logging but lethal for any per-tick / per-
            // event UI logging.
            PatternLayout patternLayout = new PatternLayout()
            {
                ConversionPattern = "%date %-5level - %message%newline"
            };

            patternLayout.ActivateOptions();

            string homeDir = Environment.ExpandEnvironmentVariables("%HOMEDRIVE%%HOMEPATH%");
            RollingFileAppender roller = new RollingFileAppender
            {
                AppendToFile = false,
                File = Path.Combine(homeDir, "sw2urdf_logs", "sw2urdf.log"),
                Layout = patternLayout,
                MaxSizeRollBackups = 5,
                MaximumFileSize = "10MB",
                RollingStyle = RollingFileAppender.RollingMode.Size,
                StaticLogFileName = true
            };

            roller.ActivateOptions();
            hierarchy.Root.AddAppender(roller);

            MemoryAppender memory = new MemoryAppender();
            memory.ActivateOptions();
            hierarchy.Root.AddAppender(memory);

            hierarchy.Root.Level = Level.Info;
            hierarchy.Configured = true;
            Initialized = true;
            ILog logger = LogManager.GetLogger(
                MethodBase.GetCurrentMethod().DeclaringType);
            logger.Info("\n" + String.Concat(Enumerable.Repeat("-", 80)));
            logger.Info("Logging commencing for SW2URDF exporter");

            logger.Info("Commit version " + Versioning.Version.GetCommitVersion());
            logger.Info("Build version " + Versioning.Version.GetBuildVersion());
        }

        public static ILog GetLogger()
        {
            Setup();
            return LogManager.GetLogger(
                MethodBase.GetCurrentMethod().DeclaringType);
        }

        public static string GetFileName()
        {
            RollingFileAppender rootAppender =
                LogManager.GetRepository().GetAppenders().OfType<RollingFileAppender>()
                                         .FirstOrDefault();
            if (rootAppender != null)
            {
                return rootAppender.File;
            }
            else
            {
                return null;
            }
        }
    }
}