using ESRI.ArcGIS.Catalog;
using ESRI.ArcGIS.CatalogUI;
using ESRI.ArcGIS.DataSourcesGDB;
using ESRI.ArcGIS.esriSystem;
using ESRI.ArcGIS.Geodatabase;
using ESRI.ArcGIS.Geometry;
using ESRI.ArcGIS.JTX;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Xml.Linq;

namespace ExportJTXTransactions
{
    public partial class ExportTransactions : Form
    {
        // Constants
        private const string DOMAIN_NAME = "TransactionTypes"; // Name of the coded value domain that will be created for transaction types
        private const esriSRGeoCSType SPATIAL_REF = esriSRGeoCSType.esriSRGeoCS_WGS1984; // SR of the output geometry

        private IJTXDatabase db = null;
        private Dictionary<string, ISpatialReference> cachedSRs = new Dictionary<string, ISpatialReference>();
        bool bInsertErrors = false;

        public ExportTransactions()
        {
            InitializeComponent();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            // Display the list of WMX connection
            IJTXDatabaseConnectionManager dbConnMgr = new JTXDatabaseConnectionManagerClass();
            cboDBs.Items.AddRange(dbConnMgr.DatabaseNames);
            cboDBs.SelectedItem = dbConnMgr.ActiveDatabaseName;
        }

        private void cmdBrowse_Click(object sender, EventArgs e)
        {
            // Pick an output file GDB
            IGxDialog pGxDialog = new GxDialogClass();
            IGxObjectFilter pFilter = new GxFilterFileGeodatabases();

            pGxDialog.AllowMultiSelect = false;
            pGxDialog.RememberLocation = true;
            pGxDialog.ButtonCaption = "Select";
            pGxDialog.Title = "Select Output File Geodatabase";
            pGxDialog.ObjectFilter = pFilter;

            IEnumGxObject pEnumGxObj;
            bool pObjSelected = pGxDialog.DoModalOpen(0, out pEnumGxObj);

            if (pObjSelected)
            {
                pEnumGxObj.Reset();
                IGxObject pGxObj = pEnumGxObj.Next();
                txtPath.Text = pGxObj.FullName;
            }
        }

        private void cmdExport_Click(object sender, EventArgs e)
        {
            bInsertErrors = false;
            string alias = cboDBs.SelectedItem.ToString();

            if (String.IsNullOrEmpty(alias) || String.IsNullOrEmpty(txtPath.Text))
            {
                MessageBox.Show("Please select a WMX Repository to export from and a file GDB to export to");
                return;
            }

            var cur = this.Cursor;
            try
            {
                this.Cursor = Cursors.WaitCursor;

                // Create the feature class to export to
                var fcs = CreateFCS(txtPath.Text);

                // Export the transaction
                Export(alias, fcs);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString());
                return;
            }
            finally
            {
                this.Cursor = cur;
            }

            if (bInsertErrors)
                MessageBox.Show("Not all transactions could be exported", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        // Create 3 feature classes to hold the point, polyline, and polygon transactions
        private Dictionary<esriGeometryType, IFeatureClass> CreateFCS(string path)
        {
            // Connect to the output file GDB
            IWorkspaceFactory wsFact = new FileGDBWorkspaceFactory();
            IWorkspace ws = wsFact.OpenFromFile(path, this.Handle.ToInt32());
            IFeatureWorkspace featws = ws as IFeatureWorkspace;

            // Get the coded value domain for the transaction type, creating if necessary
            IDomain domain = GetOrCreateDomain(ws);

            Dictionary<esriGeometryType, IFeatureClass> dictionary = new Dictionary<esriGeometryType, IFeatureClass>();
            foreach (var geomType in new esriGeometryType[] { esriGeometryType.esriGeometryPoint, esriGeometryType.esriGeometryPolyline, esriGeometryType.esriGeometryPolygon })
            {
                // Create the feature classes
                string fcName = Enum.GetName(typeof(esriGeometryType), geomType) + "_TransactionsExportedAt" + DateTime.Now.ToString("yyyyMMdd_HHmm");
                UID tableUID = new UIDClass();
                tableUID.Value = "esriGeoDatabase.Feature";

                // Get the fields for this geometry type
                IFields fields = GetFields(geomType, domain);

                // Validate the fields
                ESRI.ArcGIS.Geodatabase.IFieldChecker fieldChecker = new ESRI.ArcGIS.Geodatabase.FieldCheckerClass();
                ESRI.ArcGIS.Geodatabase.IEnumFieldError enumFieldError = null;
                ESRI.ArcGIS.Geodatabase.IFields validatedFields = null;
                fieldChecker.ValidateWorkspace = ws;
                fieldChecker.Validate(fields, out enumFieldError, out validatedFields);

                IFieldError err;
                while (enumFieldError != null && (err = enumFieldError.Next()) != null)
                {
                    // If the field validation failed, display an error (since fields are mostly hardcoded, this shouldn't really happen)
                    MessageBox.Show(Enum.GetName(typeof(esriFieldNameErrorType), err.FieldError), "Field Error");
                }

                dictionary.Add(geomType, featws.CreateFeatureClass(fcName, validatedFields, tableUID, null, esriFeatureType.esriFTSimple, "SHAPE", "DEFAULT"));
            }

            return dictionary;
        }

        #region Field Definitions
        private const int idIdx = 0;
        private const int idFieldIdx = 1;
        private const int jobIdIdx = 2;
        private const int loggedByIdx = 3;
        private const int tabNameIdx = 4;
        private const int transTypeIdx = 5;
        private const int transDateIdx = 6;
        private const int attributesIdx = 7;
        private const int sessionIdIdx = 8;

        private IFields GetFields(esriGeometryType geomType, IDomain domain)
        {
            IFieldsEdit fields = new Fields() as IFieldsEdit;
            IFieldEdit field = new Field() as IFieldEdit;

            field.AliasName_2 = "Feature ID";
            field.Name_2 = "FeatureID";
            field.Type_2 = esriFieldType.esriFieldTypeString;
            field.Length_2 = 100;

            fields.AddField(field);
            field = new Field() as IFieldEdit;

            field.AliasName_2 = "Feature ID Field";
            field.Name_2 = "FeatureIDField";
            field.Type_2 = esriFieldType.esriFieldTypeString;
            field.Length_2 = 100;

            fields.AddField(field);
            field = new Field() as IFieldEdit;

            field.AliasName_2 = "Job ID";
            field.Name_2 = "JobID";
            field.Type_2 = esriFieldType.esriFieldTypeInteger;
            field.Precision_2 = 10;

            fields.AddField(field);
            field = new Field() as IFieldEdit;

            field.AliasName_2 = "Logged By";
            field.Name_2 = "LoggedBy";
            field.Type_2 = esriFieldType.esriFieldTypeString;
            field.Length_2 = 100;

            fields.AddField(field);
            field = new Field() as IFieldEdit;

            field.AliasName_2 = "Table Name";
            field.Name_2 = "TableName";
            field.Type_2 = esriFieldType.esriFieldTypeString;
            field.Length_2 = 100;

            fields.AddField(field);
            field = new Field() as IFieldEdit;

            field.AliasName_2 = "Transaction Type";
            field.Name_2 = "TransactionType";
            field.Type_2 = esriFieldType.esriFieldTypeInteger;
            field.Domain_2 = domain;

            fields.AddField(field);
            field = new Field() as IFieldEdit;

            field.AliasName_2 = "Transaction Date";
            field.Name_2 = "TransactionDate";
            field.Type_2 = esriFieldType.esriFieldTypeDate;

            fields.AddField(field);
            field = new Field() as IFieldEdit;

            field.AliasName_2 = "Attributes";
            field.Name_2 = "Attributes";
            field.Type_2 = esriFieldType.esriFieldTypeString;
            field.Length_2 = 5000;

            fields.AddField(field);
            field = new Field() as IFieldEdit;

            field.AliasName_2 = "Session ID";
            field.Name_2 = "SessionID";
            field.Type_2 = esriFieldType.esriFieldTypeInteger;
            field.Precision_2 = 10;

            fields.AddField(field);
            field = new Field() as IFieldEdit;

            IGeometryDefEdit geomDef = new GeometryDef() as IGeometryDefEdit;
            geomDef.GeometryType_2 = geomType;
            geomDef.SpatialReference_2 = GetSpatialReference();
            geomDef.HasM_2 = false;
            geomDef.HasZ_2 = false;

            field.Name_2 = "SHAPE";
            field.Type_2 = esriFieldType.esriFieldTypeGeometry;
            field.GeometryDef_2 = geomDef;

            fields.AddField(field);

            return fields;
        }

        private static IDomain GetOrCreateDomain(IWorkspace ws)
        {
            IWorkspaceDomains wsd = ws as IWorkspaceDomains;
            IDomain domain = wsd.get_DomainByName(DOMAIN_NAME);
            if (domain == null)
            {
                ICodedValueDomain2 cvd = new CodedValueDomain() as ICodedValueDomain2;
                cvd.AddCode(1, "New Feature");
                cvd.AddCode(2, "Modified Feature");
                cvd.AddCode(3, "Modified Feature (After)");
                cvd.AddCode(6, "Modified Feature (Before)");
                cvd.AddCode(4, "Deleted Feature");
                domain = cvd as IDomain;
                domain.FieldType = esriFieldType.esriFieldTypeInteger;
                domain.Name = DOMAIN_NAME;
                domain.SplitPolicy = esriSplitPolicyType.esriSPTDuplicate;
                domain.MergePolicy = esriMergePolicyType.esriMPTDefaultValue;
                int i = wsd.AddDomain(domain);
            }
            return domain;
        }

        #endregion

        private void Export(string alias, Dictionary<esriGeometryType, IFeatureClass> fcs)
        {
            // Connect to the WMX Repository
            IJTXDatabaseManager dbMgr = new JTXDatabaseManager();
            db = dbMgr.GetDatabase(alias);

            IJTXTransactionManager transactionMgr = db.TransactionManager;
            
            // Get all transactions and insert into the appropriate ouput feature class
            IJTXTransactionSet transactions = transactionMgr.GetLoggedTransactions(new QueryFilter());
            for (int i = 0; i < transactions.Count; i++)
            {
                try
                {
                    IJTXTransaction2 trans = transactions.get_Item(i) as IJTXTransaction2;
                    InsertTransaction(trans, fcs);
                }
                catch (Exception)
                {
                    bInsertErrors = true;
                }
            }
            db = null;
        }

        private void InsertTransaction(IJTXTransaction2 trans, Dictionary<esriGeometryType, IFeatureClass> fcs)
        {
            // Currently does not do anythign with the annotation elements

            // Add and Delete transactions will only have a single geometry
            // But Modify transactions will have a before and an After geometry
            for (int i = 0; i < trans.Geometry.GeometryCount; i++)
            {
                IGeometry geom = trans.Geometry.get_Geometry(i);
                IFeatureClass fc = fcs[geom.GeometryType];
                IFeature feat = fc.CreateFeature();

                // Figure out the more detailed transaction type for modified (Before or After feature)
                jtxTransactionType transType = trans.TransactionType;
                if (transType == jtxTransactionType.jtxTransactionTypeModify)
                {
                    if (i == 0) // The first item is the before
                        transType = jtxTransactionType.jtxTransactionTypeModify | jtxTransactionType.jtxTransactionTypeDelete;
                    else // There should only be 2 items
                        transType = jtxTransactionType.jtxTransactionTypeModify | jtxTransactionType.jtxTransactionTypeAdd;
                }

                // Set the basic values
                feat.set_Value(idIdx, trans.GFID);
                feat.set_Value(idFieldIdx, trans.GFIDField);
                feat.set_Value(jobIdIdx, trans.JobID);
                feat.set_Value(loggedByIdx, trans.LoggedBy);
                feat.set_Value(tabNameIdx, trans.TableName);
                feat.set_Value(transTypeIdx, transType);
                feat.set_Value(transDateIdx, trans.TransactionDate);
                feat.set_Value(sessionIdIdx, trans.SessionID);

                IJTXTransactionAttributeSet attribs = trans.Attributes;

                // Convert the attributes to a basic XML fragment
                // This will be of the form:
                // <Attributes>
                //   <Attribute>
                //     <FieldName>Field1</FieldName>
                //     <Value>SomeValue</Value>
                //   </Attribute>
                //   ...
                // </Attributes>
                XElement attribsXML = new XElement("Attributes");
                for (int j = 0; j < attribs.Count; j++)
                {
                    IJTXTransactionAttribute attribute = attribs.get_Item(j);
                    string val;
                    // Attributes also have before and after values. Based on the transaction type, see if we need the before or after value
                    if ((transType & jtxTransactionType.jtxTransactionTypeDelete) != 0)
                        val = attribute.PreviousValue;
                    else
                        val = attribute.NewValue;

                    attribsXML.Add(
                        new XElement("Attribute",
                            new XElement("FieldName", attribute.FieldName),
                            new XElement("Value", val)
                        ));
                }
                string attribString = attribsXML.ToString(SaveOptions.DisableFormatting);
                feat.set_Value(attributesIdx, attribString == null ? DBNull.Value : (object)attribString);

                // Set the spatial reference of the geometry
                var sourceSR = GetSpatialReference(trans.TableName);
                geom.SpatialReference = sourceSR;
                var targetSR = ((IGeoDataset)fc).SpatialReference;
                if (sourceSR != null && targetSR != null && sourceSR.Name != targetSR.Name)
                    geom.Project(targetSR); // Project to the right SR

                feat.Shape = geom;

                feat.Store();

            }
        }

        // Get the default spatial reference (used for output)
        private ISpatialReference GetSpatialReference()
        {
            ISpatialReferenceFactory srFact = new SpatialReferenceEnvironment() as ISpatialReferenceFactory;

            IGeographicCoordinateSystem projectedCS = srFact.CreateGeographicCoordinateSystem(
                (int)SPATIAL_REF);
            return projectedCS;
        }

        // Get the spatial reference of a table
        private ISpatialReference GetSpatialReference(string tableName)
        {
            if (!cachedSRs.ContainsKey(tableName))
            {
                IWorkspace ws = db.get_DataWorkspace("");
                IFeatureWorkspace fws = ws as IFeatureWorkspace;
                var fc = fws.OpenFeatureClass(tableName);
                IGeoDataset geods = fc as IGeoDataset;

                if (fc == null || geods == null)
                    cachedSRs.Add(tableName, null);
                else
                    cachedSRs.Add(tableName, geods.SpatialReference);

            }
            return cachedSRs[tableName];
        }

    }
}
