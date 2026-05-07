using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SW2URDF.Configuration;
using SW2URDF.Legacy;
using SW2URDF.URDF;
using SW2URDF.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using System.Windows.Forms;
using System.Xml;
using System.Xml.Serialization;

namespace SW2URDF.URDFExport
{
    /// <summary>
    /// Persistence boundary between the in-memory PMPage tree and SolidWorks
    /// document attributes. Phase 2 promoted ConfigV2 JSON to the canonical
    /// save format; the legacy v1.3-v1.5 DataContract XML and the very-old
    /// XmlSerializer SerialNode formats remain read-only fallbacks so an
    /// existing assembly's saved configuration still loads after upgrade.
    /// First save after a load migrates onto v2 by writing the new attribute
    /// alongside (not replacing) the old one.
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
        /// SolidWorks attribute name for ConfigV2 JSON. Parallel storage:
        /// new saves ALWAYS write here; existing v1.5 attributes are
        /// retained for downgrade safety. <see cref="LoadBaseNodeFromModel"/>
        /// prefers v2 over the legacy attribute.
        /// </summary>
        public const string UrdfConfigurationV2SwAttributeName = "URDF Export Configuration (v2)";

        /// <summary>
        /// SolidWorks attribute name for the legacy v1.3-v1.5 DataContract
        /// XML schema. Read-only after Phase 2; new saves go to the v2
        /// JSON attribute instead.
        /// </summary>
        public const string UrdfConfigurationSwAttributeName = "URDF Export Configuration (v1.5)";

        public static List<string> PREVIOUS_URDF_CONFIGURATION_NAMES = new List<string>() {
            "URDF Export Configuration (v1.4)",
            "URDF Export Configuration (v1.3)",
            "URDF Export Configuration"
        };

        #region Public Methods

        /// <summary>
        /// Loads the URDF tree from the SW Model Document. Tries v2 JSON
        /// first; falls back to v1.3-v1.5 DataContract XML; falls back to
        /// the very-old XmlSerializer format.
        /// </summary>
        public static LinkNode LoadBaseNodeFromModel(ModelDoc2 model, out bool error)
        {
            error = false;

            // Phase 2: prefer the JSON v2 attribute when present.
            string jsonData = ReadAttributeData(model, UrdfConfigurationV2SwAttributeName);
            if (!string.IsNullOrWhiteSpace(jsonData))
            {
                try
                {
                    ConfigV2 config = ConfigV2JsonSerializer.Deserialize(jsonData);
                    return ConfigV2Bridge.CreateLinkNode(config);
                }
                catch (Exception ex)
                {
                    logger.Error("Failed to read ConfigV2 JSON; falling back to legacy XML.", ex);
                    logger.Error(jsonData);
                    // Fall through to the legacy attribute lookup.
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
                return DeserializeFromString(legacyData);
            }
            return LoadConfigFromStringXML(legacyData);
        }

        /// <summary>
        /// Saves the LinkNode tree to the active model. Phase 2 always
        /// writes ConfigV2 JSON to the v2 attribute; the v1.5 attribute is
        /// not touched (read-only fallback). The first save after a legacy
        /// load is therefore the migration step.
        /// </summary>
        public static void SaveConfigTreeXML(SldWorks swApp, ModelDoc2 model, LinkNode BaseNode, bool warnUser)
        {
            if (BaseNode == null)
            {
                return;
            }

            string oldData = ReadAttributeData(model, UrdfConfigurationV2SwAttributeName);
            string oldLegacy = ReadAttributeData(model, UrdfConfigurationSwAttributeName);

            // Heuristic for the "you have an old config that will be
            // upgraded" warning: only fire when there's no v2 attribute yet
            // AND a legacy XML attribute is present. This matches the
            // existing UX: users who never saved before see no popup;
            // users mid-migration see one informational popup on first save.
            if (string.IsNullOrWhiteSpace(oldData) && !string.IsNullOrWhiteSpace(oldLegacy))
            {
                MessageBox.Show("Your URDF/MJCF Export configuration was saved in an older XML " +
                    "format. It will be migrated to the new JSON format under \"" +
                    UrdfConfigurationV2SwAttributeName + "\". The old attribute is preserved " +
                    "so an older exporter version can still read it; you can delete it later " +
                    "from the FeatureManager.");
                warnUser = false;
            }

            string newData;
            try
            {
                ConfigV2 config = ConfigV2Bridge.CreateFromLinkNode(BaseNode, model?.GetTitle());
                newData = ConfigV2JsonSerializer.Serialize(config);
            }
            catch (Exception ex)
            {
                logger.Error("Serializing ConfigV2 JSON failed", ex);
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
                    SaveDataToModelDoc(swApp, model, UrdfConfigurationV2SwAttributeName, newData);
                }
            }
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
        // configurations saved before MinDataContractVersion.
        private static LinkNode LoadConfigFromStringXML(string data)
        {
            LinkNode baseNode = null;
            if (!string.IsNullOrWhiteSpace(data))
            {
                XmlSerializer serializer = new XmlSerializer(typeof(SerialNode));
                XmlTextReader textReader = new XmlTextReader(new StringReader(data));
                // Not reading external files, so this can be set to prohibit. Resolves CA3075
                textReader.DtdProcessing = DtdProcessing.Prohibit;
                SerialNode sNode = (SerialNode)serializer.Deserialize(textReader);
                textReader.Close();

                baseNode = sNode.BuildLinkNodeFromSerialNode();
            }
            return baseNode;
        }

        private static SolidWorks.Interop.sldworks.Attribute CheckForOldAttributes(ModelDoc2 model)
        {
            foreach (string configurationName in PREVIOUS_URDF_CONFIGURATION_NAMES)
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
        // for the v2 JSON attribute now; legacy attributes are read-only.
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
            // cheaply tell v2 JSON from v1.5 XML without having to parse the
            // payload first.
            saveConfigurationAttributeDef.AddParameter(
                "exporterVersion", (int)swParamType_e.swParamTypeDouble,
                ConfigV2.CurrentSchemaVersion, Options);
            saveConfigurationAttributeDef.Register();

            return saveConfigurationAttributeDef.CreateInstance5(
                model, null, attributeName, Options, ConfigurationOptions);
        }

        // Saves a string of data to the named attribute on the active doc.
        // Always writes the v2 attribute; legacy attributes are read-only.
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
            param.SetDoubleValue2(ConfigV2.CurrentSchemaVersion, ConfigurationOptions, "");
        }

        #endregion Private Methods
    }
}
