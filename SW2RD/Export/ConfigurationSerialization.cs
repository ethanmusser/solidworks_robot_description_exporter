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
using SW2RD.Input;
using SW2RD.Utilities;
using System;
using System.Windows.Forms;

namespace SW2RD.Export
{
    /// <summary>
    /// Persistence boundary between the in-memory PMPage tree and SolidWorks
    /// document attributes. The only supported format is the SW2RD v1 JSON
    /// schema (<see cref="Config"/>); legacy SW2URDF v2 JSON / v1.3-v1.5
    /// DataContract XML / pre-v1.3 SerialNode import was removed in the
    /// KinematicTree refactor.
    /// </summary>
    public static class ConfigurationSerialization
    {
        private static readonly log4net.ILog logger = Logger.GetLogger();

        /// <summary>
        /// SolidWorks attribute name for the SW2RD v1 JSON schema. The only
        /// attribute name read or written. <see cref="LoadBaseNodeFromModel"/>
        /// loads it; <see cref="SaveConfigTreeXML"/> writes it.
        /// </summary>
        public const string ConfigurationSwAttributeName = "SW2RD Export Configuration (v1)";

        #region Public Methods

        /// <summary>
        /// Loads the canonical SW2RD v1 JSON tree from the SW Model Document.
        /// </summary>
        public static LinkNode LoadBaseNodeFromModel(ModelDoc2 model, out bool error)
        {
            error = false;

            // Canonical SW2RD v1 JSON.
            return TryLoadV2Json(model, ConfigurationSwAttributeName);
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
        // parse as JSON (the leading-'{' payload-shape check guards against a
        // non-JSON attribute payload).
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
        /// Saves the LinkNode tree to the active model as the canonical
        /// SW2RD v1 JSON attribute.
        /// </summary>
        public static void SaveConfigTreeXML(SldWorks swApp, ModelDoc2 model, LinkNode BaseNode, bool warnUser)
        {
            if (BaseNode == null)
            {
                return;
            }

            string oldData = ReadAttributeData(model, ConfigurationSwAttributeName);

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
