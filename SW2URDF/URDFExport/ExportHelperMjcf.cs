/*
Copyright (c) 2015 Stephen Brawner

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.  IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
*/

using MathNet.Numerics.LinearAlgebra;
using SolidWorks.Interop.sldworks;
using SW2URDF.MJCF;
using SW2URDF.URDF;
using SW2URDF.Utilities;
using System;
using System.Collections.Generic;

namespace SW2URDF.URDFExport
{
    // MJCF flow entry point. Kept in a separate partial-class file so the URDF export path in
    // ExportHelper.cs is untouched; the two flows share STL-preference helpers in the same class
    // but never diverge from a shared orchestration method.
    public partial class ExportHelper
    {
        /// <summary>
        /// Writes an MJCF export of the currently held <see cref="URDFRobot"/> tree to
        /// <c>{SavePath}/{PackageName}/{PackageName}.xml</c> (with STL meshes next to it).
        /// </summary>
        public void ExportMjcf(MjcfOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            logger.Info("Beginning MJCF export process");
            int progressBarBound = CommonSwOperations.GetCount(URDFRobot.BaseLink);
            iSwApp.GetUserProgressBar(out progressBar);
            progressBar.Start(0, progressBarBound, "Creating MJCF package directories");

            MjcfPackage package = new MjcfPackage(PackageName, SavePath, options.MeshDir);
            package.CreateDirectories();
            URDFRobot.Name = PackageName;

            logger.Info("Saving existing STL preferences");
            SaveUserPreferences();

            logger.Info("Modifying STL preferences");
            SetSTLExportPreferences();

            AssemblyDoc assyDoc = (AssemblyDoc)ActiveSWModel;
            List<string> hiddenComponents = CommonSwOperations.FindHiddenComponents(assyDoc.GetComponents(false));
            logger.Info("Found " + hiddenComponents.Count + " hidden components " + string.Join(", ", hiddenComponents));
            logger.Info("Hiding all components");
            ActiveSWModel.Extension.SelectAll();
            ActiveSWModel.HideComponent2();

            Dictionary<string, string> linkMeshFilenames = new Dictionary<string, string>();
            bool success = false;
            try
            {
                logger.Info("Beginning individual mesh export for MJCF");
                ExportMjcfMeshes(URDFRobot.BaseLink, package, 0, linkMeshFilenames);
                success = true;
            }
            catch (Exception e)
            {
                logger.Error("An exception was thrown attempting to export MJCF meshes", e);
            }
            finally
            {
                logger.Info("Showing all components except previously hidden components");
                CommonSwOperations.ShowAllComponents(ActiveSWModel, hiddenComponents);

                logger.Info("Resetting STL preferences");
                ResetUserPreferences();
            }

            if (!success)
            {
                System.Windows.Forms.MessageBox.Show("Exporting the MJCF failed unexpectedly. " +
                    "Email your maintainer with the log file found at " + Logger.GetFileName());
                return;
            }

            // Site poses require live SolidWorks coord-system lookups, so they are resolved here
            // (not in MjcfWriter) and handed to the writer as plain data.
            Dictionary<string, List<MjcfSite>> linkSites = ResolveAllSites(URDFRobot.BaseLink);

            logger.Info("Writing MJCF file to " + package.WindowsXmlFileName);
            MjcfWriter.Write(URDFRobot, options, package.WindowsXmlFileName, linkSites, linkMeshFilenames);

            progressBar.End();
        }

        private void ExportMjcfMeshes(
            Link link,
            MjcfPackage package,
            int count,
            Dictionary<string, string> linkMeshFilenames)
        {
            progressBar.UpdateProgress(count);
            progressBar.UpdateTitle("Exporting mesh: " + link.Name);
            logger.Info("Exporting MJCF link: " + link.Name);
            foreach (Link child in link.Children)
            {
                count += 1;
                if (!child.isFixedFrame)
                {
                    ExportMjcfMeshes(child, package, count, linkMeshFilenames);
                }
            }

            if (link.isFixedFrame)
            {
                return;
            }

            // SolidWorks permits '/' in link names, but using it in a filename produces broken
            // paths; mirror the URDF flow's substitution.
            string linkName = link.Name.Replace('/', '_');
            string stlBasename = linkName + ".STL";
            string windowsMeshFileName = package.WindowsMeshesDirectory + stlBasename;

            SaveSTL(link, windowsMeshFileName);

            // Give the MJCF asset block an unambiguous basename; MjcfWriter resolves this against
            // <compiler meshdir="..."/> in the emitted XML.
            linkMeshFilenames[link.Name] = stlBasename;
            if (link.Visual?.Geometry?.Mesh != null)
            {
                link.Visual.Geometry.Mesh.Filename = stlBasename;
            }
            if (link.Collision?.Geometry?.Mesh != null)
            {
                link.Collision.Geometry.Mesh.Filename = stlBasename;
            }
        }

        private Dictionary<string, List<MjcfSite>> ResolveAllSites(Link baseLink)
        {
            Dictionary<string, List<MjcfSite>> byLink = new Dictionary<string, List<MjcfSite>>();
            ResolveSitesRecursive(baseLink, byLink);
            return byLink;
        }

        private void ResolveSitesRecursive(Link link, Dictionary<string, List<MjcfSite>> byLink)
        {
            if (link.SiteCoordSystemNames != null && link.SiteCoordSystemNames.Count > 0)
            {
                List<MjcfSite> sites = ResolveSitesForLink(link);
                if (sites.Count > 0)
                {
                    byLink[link.Name] = sites;
                }
            }
            foreach (Link child in link.Children)
            {
                ResolveSitesRecursive(child, byLink);
            }
        }

        private List<MjcfSite> ResolveSitesForLink(Link link)
        {
            List<MjcfSite> result = new List<MjcfSite>();
            string linkCoordSysName = link.Joint?.CoordinateSystemName;
            if (string.IsNullOrWhiteSpace(linkCoordSysName))
            {
                logger.Warn($"Link {link.Name} has no coord system; cannot resolve sites.");
                return result;
            }

            Matrix<double> linkGlobal;
            try
            {
                MathTransform linkTransform = GetCoordinateSystemTransform(linkCoordSysName);
                if (linkTransform == null)
                {
                    logger.Warn($"Link {link.Name}: coord system '{linkCoordSysName}' did not resolve.");
                    return result;
                }
                linkGlobal = MathOps.GetTransformation(linkTransform);
            }
            catch (Exception ex)
            {
                logger.Warn($"Failed to resolve link coord system for {link.Name}", ex);
                return result;
            }
            Matrix<double> linkGlobalInverse = linkGlobal.Inverse();

            HashSet<string> emittedNames = new HashSet<string>();
            foreach (string siteCoordName in link.SiteCoordSystemNames)
            {
                if (string.IsNullOrWhiteSpace(siteCoordName))
                {
                    continue;
                }
                try
                {
                    MathTransform siteTransform = GetCoordinateSystemTransform(siteCoordName);
                    if (siteTransform == null)
                    {
                        logger.Warn($"Site '{siteCoordName}' on link {link.Name} did not resolve.");
                        continue;
                    }
                    Matrix<double> siteGlobal = MathOps.GetTransformation(siteTransform);
                    Matrix<double> siteInLink = linkGlobalInverse * siteGlobal;
                    double[] xyz = MathOps.GetXYZ(siteInLink);
                    double[] rpy = MathOps.GetRPY(siteInLink);

                    // Strip the SolidWorks "<component>" suffix so the MJCF identifier is legal and
                    // matches what the user typed in the Sites check list.
                    string bareName = StripComponentSuffix(siteCoordName);
                    string uniqueName = bareName;
                    int suffixNumber = 2;
                    while (!emittedNames.Add(uniqueName))
                    {
                        uniqueName = bareName + "_" + suffixNumber;
                        suffixNumber++;
                    }
                    result.Add(new MjcfSite(uniqueName, xyz, rpy));
                }
                catch (Exception ex)
                {
                    logger.Warn($"Failed to resolve site '{siteCoordName}' on link {link.Name}", ex);
                }
            }
            return result;
        }

        private static string StripComponentSuffix(string coordSysName)
        {
            if (string.IsNullOrWhiteSpace(coordSysName))
            {
                return coordSysName;
            }
            int openIdx = coordSysName.IndexOf('<');
            if (openIdx <= 0)
            {
                return coordSysName.Trim();
            }
            return coordSysName.Substring(0, openIdx).Trim();
        }
    }
}
