/*
Copyright (c) 2026 Ethan J. Musser

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

using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SW2RD.Configuration;
using SW2RD.Legacy;
using SW2RD.URDF;
using SW2RD.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using System.Windows.Forms;
using System.Xml;
using System.Xml.Serialization;

namespace SW2RD.Export
{
    /// <summary>
    /// Persistence boundary between the in-memory PMPage tree and SolidWorks
    /// document attributes. The canonical save format is the SW2RD v1 JSON
    /// schema (<see cref="Config"/>); the legacy SW2URDF v2 JSON, v1.3-v1.5
    /// DataContract XML, and very-old XmlSerializer SerialNode formats remain
    /// read-only explicit import sources. First save after importing a legacy
    /// attribute migrates onto the SW2RD v1 attribute (written alongside, not
    /// replacing, the old one).
    /// </summary>
    public static class ConfigurationSerialization
    {
        private static readonly log4net.ILog logger = Logger.GetLogger();

        // Format version of the legacy DataContract attribute. Kept as a
        // double for backward compatibility with the old `exporterVersion`
        // attribute parameter on existing documents.
        private const double SerializationVersion = 1.5;

        // The very-old XmlSerializer (SerialNode) shape was stored under
        // exporterVersion < 1.3. v1.3 marks the cutover to DataContract XML.
        private const double MinDataContractVersion = 1.3;

        /// <summary>
        /// SolidWorks attribute name for the SW2RD v1 JSON schema. New saves
        /// ALWAYS write here; existing v1.5 XML, legacy SW2URDF v2 JSON, and
        /// any earlier pre-rebrand SW2RD attributes are retained read-only
        /// for downgrade safety. <see cref="LoadBaseNodeFromModel"/> prefers
        /// this over any legacy attribute.
        /// </summary>
        public const string ConfigurationSwAttributeName = "SW2RD Export Configuration (v1)";

        /// <summary>
        /// SolidWorks attribute name for the legacy v1.3-v1.5 DataContract
        /// XML schema. Read-only; new saves go to the SW2RD v1 JSON attribute
        /// instead. The literal "URDF Export Configuration (v1.5)" string is
        /// wire-format and never renames.
        /// </summary>
        public const string UrdfConfigurationSwAttributeName = "URDF Export Configuration (v1.5)";

        /// <summary>
        /// Read-only legacy attribute names tried in order when the canonical
        /// SW2RD v1 attribute is absent. Entries are tried as JSON first
        /// (payload-shape probe in <see cref="TryLoadV2Json"/>); names whose
        /// payloads aren't JSON fall through to the legacy DataContract XML
        /// reader. Order: pre-rebrand SW2RD JSON, then SW2URDF v2 JSON, then
        /// progressively older URDF DataContract XML.
        /// </summary>
        public static List<string> PREVIOUS_CONFIGURATION_NAMES = new List<string>() {
            "SW2RD Export Configuration (v2)",
            "URDF Export Configuration (v2)",
            "URDF Export Configuration (v1.4)",
            "URDF Export Configuration (v1.3)",
            "URDF Export Configuration"
        };

        #region Public Methods

        /// <summary>
        /// Loads only the canonical SW2RD v1 JSON tree from the SW Model
        /// Document. Legacy SW2URDF / pre-rebrand attributes are intentionally
        /// not probed here; users import those explicitly from the Setup tab.
        /// </summary>
        public static LinkNode LoadBaseNodeFromModel(ModelDoc2 model, out bool error)
        {
            error = false;

            // Canonical SW2RD v1 JSON.
            return TryLoadV2Json(model, ConfigurationSwAttributeName);
        }

        /// <summary>
        /// Explicit one-shot importer for legacy pre-rebrand SW2RD /
        /// SW2URDF attributes. This preserves backward compatibility without
        /// making legacy import the default PMPage startup behavior.
        /// </summary>
        public static LinkNode LoadLegacyBaseNodeFromModel(ModelDoc2 model, out bool error)
        {
            error = false;

            // Probe each legacy attribute name as JSON first. Older entries
            // in PREVIOUS_CONFIGURATION_NAMES are DataContract XML names; the
            // payload-shape heuristic inside TryLoadV2Json rejects XML so
            // those names fall through to the legacy XML reader below via
            // GetLegacyConfigTreeData / CheckForOldAttributes.
            foreach (string legacyName in PREVIOUS_CONFIGURATION_NAMES)
            {
                LinkNode migrated = TryLoadV2Json(model, legacyName);
                if (migrated != null)
                {
                    logger.Info("Migrating legacy JSON config from attribute \"" + legacyName +
                        "\"; next save will write to \"" + ConfigurationSwAttributeName + "\".");
                    return ApplyLegacyCollisionUsesVisualDefault(migrated);
                }
            }

            // Legacy DataContract XML / SerialNode XML branches.
            string legacyData = GetLegacyConfigTreeData(model, out double legacyVersion);
            if (legacyVersion > SerializationVersion)
            {
                MessageBox.Show("The configuration saved in this model is newer than what this " +
                    "exporter supports " + string.Format("({0} > {1})", legacyVersion, SerializationVersion) +
                    ". Please update your exporter version");
                error = true;
                return null;
            }

            if (legacyVersion >= MinDataContractVersion)
            {
                return ApplyLegacyCollisionUsesVisualDefault(DeserializeFromString(legacyData));
            }
            return ApplyLegacyCollisionUsesVisualDefault(LoadConfigFromStringXML(legacyData));
        }

        private static LinkNode ApplyLegacyCollisionUsesVisualDefault(LinkNode node)
        {
            SetCollisionUsesVisualDefault(node);
            return node;
        }

        private static void SetCollisionUsesVisualDefault(LinkNode node)
        {
            if (node == null)
            {
                return;
            }
            if (node.Link != null)
            {
                node.Link.CollisionUsesVisual = Link.DefaultCollisionUsesVisual;
            }
            foreach (TreeNode child in node.Nodes)
            {
                SetCollisionUsesVisualDefault(child as LinkNode);
            }
        }

        /// <summary>
        /// Returns true when the model carries any legacy export configuration
        /// attribute that can be imported explicitly.
        /// </summary>
        public static bool HasLegacyConfiguration(ModelDoc2 model)
        {
            if (model == null)
            {
                return false;
            }
            return CheckForOldAttributes(model) != null;
        }

        /// <summary>
        /// Returns true when the model has the canonical SW2RD v1 JSON
        /// configuration attribute.
        /// </summary>
        public static bool HasSavedConfiguration(ModelDoc2 model)
        {
            if (model == null)
            {
                return false;
            }
            return FindSWSaveAttribute(model, ConfigurationSwAttributeName) != null;
        }

        /// <summary>
        /// Deletes the canonical SW2RD v1 JSON configuration attribute from
        /// the model. Legacy attributes are left untouched so the user can
        /// still import them explicitly after clearing the SW2RD cache.
        /// </summary>
        public static bool ClearSavedConfiguration(ModelDoc2 model)
        {
            if (model == null)
            {
                return false;
            }

            Feature feature = GetFeatureAttributeByName(model, ConfigurationSwAttributeName);
            if (feature == null)
            {
                return false;
            }

            try
            {
                model.ClearSelection2(true);
                bool selected = feature.Select2(false, 0);
                if (!selected)
                {
                    logger.Warn("Could not select SW2RD configuration attribute for deletion.");
                    return false;
                }
                model.EditDelete();
                model.ClearSelection2(true);
                return true;
            }
            catch (Exception ex)
            {
                logger.Warn("Failed to clear SW2RD configuration attribute.", ex);
                return false;
            }
        }

        // Reads `attributeName` and attempts to deserialize as SW2RD Config
        // JSON. Returns null if the attribute is missing, empty, or does not
        // parse as JSON. The payload-shape check (leading '{') keeps XML
        // attributes in PREVIOUS_CONFIGURATION_NAMES from being misrouted
        // here. The function name preserves "V2Json" for git-blame continuity
        // with the SW2URDF era; the schema it parses today is SW2RD v1, but
        // the payload shape is identical to SW2URDF v2 (renumbered, not
        // restructured), so the same parser handles both attribute lineages.
        private static LinkNode TryLoadV2Json(ModelDoc2 model, string attributeName)
        {
            string jsonData = ReadAttributeData(model, attributeName);
            if (string.IsNullOrWhiteSpace(jsonData))
            {
                return null;
            }
            string trimmed = jsonData.TrimStart();
            if (trimmed.Length == 0 || trimmed[0] != '{')
            {
                return null;
            }
            try
            {
                Config config = ConfigJsonSerializer.Deserialize(jsonData);
                return ConfigBridge.CreateLinkNode(config);
            }
            catch (Exception ex)
            {
                logger.Error("Failed to read Config JSON from attribute \"" + attributeName +
                    "\"; falling through to next probe.", ex);
                logger.Error(jsonData);
                return null;
            }
        }

        /// <summary>
        /// Saves the LinkNode tree to the active model. Always writes the
        /// canonical SW2RD v1 JSON attribute; legacy attributes (v1.5 XML,
        /// SW2URDF v2 JSON, pre-rebrand SW2RD v2 JSON) are read-only
        /// fallbacks. The first save after a legacy load is therefore the
        /// migration step.
        /// </summary>
        public static void SaveConfigTreeXML(SldWorks swApp, ModelDoc2 model, LinkNode BaseNode, bool warnUser)
        {
            if (BaseNode == null)
            {
                return;
            }

            string oldData = ReadAttributeData(model, ConfigurationSwAttributeName);
            string oldLegacy = ReadAttributeData(model, UrdfConfigurationSwAttributeName);

            // One-shot migration notice: only warn when the canonical
            // SW2RD v1 attribute is absent and a legacy XML attribute is
            // present. Users see the informational popup on the first save
            // that writes the new JSON attribute.
            if (string.IsNullOrWhiteSpace(oldData) && !string.IsNullOrWhiteSpace(oldLegacy))
            {
                MessageBox.Show("Your Robot Description Export configuration was saved in an older XML " +
                    "format. It will be migrated to the new JSON format under \"" +
                    ConfigurationSwAttributeName + "\". The old attribute is preserved " +
                    "so an older exporter version can still read it; you can delete it later " +
                    "from the FeatureManager.");
                warnUser = false;
            }

            string newData;
            try
            {
                Config config = ConfigBridge.CreateFromLinkNode(BaseNode, model?.GetTitle());
                newData = ConfigJsonSerializer.Serialize(config);
            }
            catch (Exception ex)
            {
                logger.Error("Serializing Config JSON failed", ex);
                MessageBox.Show("Serializing this configuration failed. Please email your " +
                    "maintainer with your SW assembly.\n\n" + ex.Message);
                return;
            }

            if (string.IsNullOrEmpty(newData))
            {
                MessageBox.Show("Serializing this link failed. Please email your maintainer with your SW assembly.");
                return;
            }

            if (oldData != newData)
            {
                if (!warnUser ||
                    (warnUser &&
                    MessageBox.Show("The configuration has changed, would you like to save?",
                    "Save Export Configuration", MessageBoxButtons.YesNo) == DialogResult.Yes))
                {
                    SaveDataToModelDoc(swApp, model, ConfigurationSwAttributeName, newData);
                }
            }
        }

        // Serializes the current PMPage tree to canonical Config JSON. Kept as
        // a small helper because tests invoke it directly via reflection and
        // ConfigBridge documents this as the pre-write normalization path.
        private static string SerializeToString(LinkNode baseNode)
        {
            if (baseNode == null)
            {
                return string.Empty;
            }
            Config config = ConfigBridge.CreateFromLinkNode(baseNode, "robot");
            return ConfigJsonSerializer.Serialize(config);
        }

        #endregion Public Methods

        #region Private Methods

        // Pulls the `data` parameter (string) of the named SW attribute or
        // returns an empty string if the attribute does not exist.
        private static string ReadAttributeData(ModelDoc2 model, string attributeName)
        {
            SolidWorks.Interop.sldworks.Attribute swAtt = FindSWSaveAttribute(model, attributeName);
            if (swAtt == null)
            {
                return string.Empty;
            }
            Parameter param = swAtt.GetParameter("data");
            return param?.GetStringValue() ?? string.Empty;
        }

        /// <summary>
        /// Reads the legacy v1.3-v1.5 DataContract attribute (or older
        /// SerialNode XML attribute) and returns its raw payload + the
        /// `exporterVersion` parameter for cutover routing.
        /// </summary>
        private static string GetLegacyConfigTreeData(ModelDoc2 model, out double version)
        {
            string data = "";
            version = 0.0;

            SolidWorks.Interop.sldworks.Attribute swAtt =
                FindSWSaveAttribute(model, UrdfConfigurationSwAttributeName);

            if (swAtt == null)
            {
                swAtt = CheckForOldAttributes(model);
            }

            if (swAtt != null)
            {
                Parameter param = swAtt.GetParameter("data");
                data = param.GetStringValue();
                logger.Info("URDF Configuration found\n" + data);

                param = swAtt.GetParameter("exporterVersion");
                version = param.GetDoubleValue();
            }

            return data;
        }

        // DataContract XML deserialization. Routes through
        // LegacyConfigV15DataContractReader so the legacy XML codec lives
        // in one place.
        private static LinkNode DeserializeFromString(string data)
        {
            LinkNode baseNode = null;
            if (!string.IsNullOrWhiteSpace(data))
            {
                try
                {
                    baseNode = LegacyConfigV15DataContractReader.ReadBaseNode(data);
                }
                catch (SerializationException e)
                {
                    logger.Error("Deserialization failed with exception, returning empty LinkNode", e);
                    logger.Error(data);
                }
            }
            return baseNode;
        }

        // Very-legacy XmlSerializer (SerialNode) deserialization for
        // configurations saved before MinDataContractVersion. The result is
        // migrated into the new WorldNode-rooted shape so the rest of the
        // pipeline always sees a consistent tree (WorldNode on top, Welded
        // top-level body inheriting the old base link's coord-sys as the
        // world's global-origin coord-sys).
        private static LinkNode LoadConfigFromStringXML(string data)
        {
            if (string.IsNullOrWhiteSpace(data))
            {
                return null;
            }

            XmlSerializer serializer = new XmlSerializer(typeof(SerialNode));
            XmlTextReader textReader = new XmlTextReader(new StringReader(data));
            // Not reading external files, so this can be set to prohibit. Resolves CA3075
            textReader.DtdProcessing = DtdProcessing.Prohibit;
            SerialNode sNode = (SerialNode)serializer.Deserialize(textReader);
            textReader.Close();

            LinkNode legacyBaseNode = sNode.BuildLinkNodeFromSerialNode();
            if (legacyBaseNode == null)
            {
                return null;
            }

            // Reuse the same migration helper as the v1.5 DataContract path.
            // legacyBaseNode.Link already carries the legacy joint coord-sys
            // (today's "global origin" placeholder), so the WorldNode wrapper
            // inherits it as GlobalOriginCoordinateSystemName for byte-identical
            // MJCF on welded single-tree configs.
            legacyBaseNode.UpdateLinkTree(null);
            return LegacyConfigV15DataContractReader.WrapLegacyBaseLinkInWorldNode(legacyBaseNode.Link);
        }

        private static SolidWorks.Interop.sldworks.Attribute CheckForOldAttributes(ModelDoc2 model)
        {
            foreach (string configurationName in PREVIOUS_CONFIGURATION_NAMES)
            {
                SolidWorks.Interop.sldworks.Attribute swAtt = FindSWSaveAttribute(model, configurationName);
                if (swAtt != null)
                {
                    return swAtt;
                }
            }
            return null;
        }

        private static Feature GetFeatureAttributeByName(ModelDoc2 model, string featName)
        {
            Object[] objects = model.FeatureManager.GetFeatures(true);
            foreach (Object obj in objects)
            {
                Feature feature = (Feature)obj;
                if (feature.GetTypeName2() == "Attribute")
                {
                    SolidWorks.Interop.sldworks.Attribute att =
                        (SolidWorks.Interop.sldworks.Attribute)feature.GetSpecificFeature2();
                    if (att.GetName() == featName)
                    {
                        return feature;
                    }
                }
            }
            return null;
        }

        private static SolidWorks.Interop.sldworks.Attribute
            FindSWSaveAttribute(ModelDoc2 model, string name)
        {
            Feature feature = GetFeatureAttributeByName(model, name);

            if (feature == null)
            {
                return null;
            }
            return (SolidWorks.Interop.sldworks.Attribute)feature.GetSpecificFeature2();
        }

        // Builds a SW Attribute for saving our serialized data. Used only
        // for the SW2RD v1 JSON attribute now; legacy attributes are read-only.
        private static SolidWorks.Interop.sldworks.Attribute
            CreateSWSaveAttribute(SldWorks swApp, ModelDoc2 model, string attributeName)
        {
            SolidWorks.Interop.sldworks.Attribute existingAttribute =
                FindSWSaveAttribute(model, attributeName);
            if (existingAttribute != null)
            {
                return existingAttribute;
            }

            int ConfigurationOptions = (int)swInConfigurationOpts_e.swAllConfiguration;
            int Options = 0;

            AttributeDef saveConfigurationAttributeDef = swApp.DefineAttribute(attributeName);
            saveConfigurationAttributeDef.AddParameter(
                "data", (int)swParamType_e.swParamTypeString, 0, Options);
            saveConfigurationAttributeDef.AddParameter(
                "name", (int)swParamType_e.swParamTypeString, 0, Options);
            saveConfigurationAttributeDef.AddParameter(
                "date", (int)swParamType_e.swParamTypeString, 0, Options);
            // Schema-version stamp on the attribute itself so a reader can
            // cheaply tell SW2RD v1 JSON from legacy XML without having to
            // parse the payload first.
            saveConfigurationAttributeDef.AddParameter(
                "exporterVersion", (int)swParamType_e.swParamTypeDouble,
                Config.CurrentSchemaVersion, Options);
            saveConfigurationAttributeDef.Register();

            return saveConfigurationAttributeDef.CreateInstance5(
                model, null, attributeName, Options, ConfigurationOptions);
        }

        // Saves a string of data to the named attribute on the active doc.
        // Always writes the SW2RD v1 attribute; legacy attributes are read-only.
        private static void SaveDataToModelDoc(SldWorks swApp, ModelDoc2 model,
            string attributeName, string data)
        {
            int ConfigurationOptions = (int)swInConfigurationOpts_e.swAllConfiguration;
            SolidWorks.Interop.sldworks.Attribute saveExporterAttribute =
                CreateSWSaveAttribute(swApp, model, attributeName);

            Parameter param = saveExporterAttribute.GetParameter("data");
            param.SetStringValue2(data, ConfigurationOptions, "");
            param = saveExporterAttribute.GetParameter("name");
            param.SetStringValue2("config1", ConfigurationOptions, "");
            param = saveExporterAttribute.GetParameter("date");
            param.SetStringValue2(DateTime.Now.ToString(), ConfigurationOptions, "");
            param = saveExporterAttribute.GetParameter("exporterVersion");
            param.SetDoubleValue2(Config.CurrentSchemaVersion, ConfigurationOptions, "");
        }

        #endregion Private Methods
    }
}
