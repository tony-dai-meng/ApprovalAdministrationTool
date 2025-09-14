using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using XrmToolBox.Extensibility;
using ApprovalAdministrationTool.Other.enums;

namespace ApprovalAdministrationTool.Functions.ApprovalDataGrid 
{
    internal class ApprovalDataGrid : PluginControlBase
    {
        private EntityCollection entity;
        private DataTable dataTable;
        private DataTable simpleDataTable;
        private AttributeMetadata[] attributesMeta;
        public ApprovalDataGrid(EntityCollection entity, AttributeMetadata[] attributesMeta)
        {
            this.entity = entity;
            this.attributesMeta = attributesMeta;
            this.dataTable = this.GetDataTableFromEntityCollection(attributesMeta);
        }

        /// <summary>
        /// This method converts an Entity Collection object to a DataTable object.
        /// </summary>
        /// <param name="entityCollection">The entity collection object</param>
        /// <returns></returns>
        private DataTable GetDataTableFromEntityCollection(AttributeMetadata[] attributesMeta)
        {
            // Init datatable
            DataTable dataTable = new DataTable("Entities");

            // Add columns to the DataTable based on the order of ApprovalColumnsLogicalName enum
            if (this.entity.Entities.Count > 0)
            {
                // Get the enum values in order
                var enumValues = Enum.GetValues(typeof(ApprovalColumnsLogicalName)).Cast<ApprovalColumnsLogicalName>().ToList();

                // Create a dictionary for quick lookup of attribute metadata by logical name
                var attributeMetaDict = attributesMeta.ToDictionary(a => a.LogicalName, a => a);

                foreach (var enumValue in enumValues)
                {
                    string logicalName = enumValue.ToString();
                    if (attributeMetaDict.TryGetValue(logicalName, out var attribute))
                    {
                        var type = ApprovalDataGrid.ToSystemType(attribute.AttributeType);
                        switch (attribute.AttributeType)
                        {
                            case AttributeTypeCode.Lookup:
                            case AttributeTypeCode.Customer:
                            case AttributeTypeCode.Owner:
                                type = typeof(string);
                                dataTable.Columns.Add(attribute.LogicalName, type);
                                break;
                            default:
                                dataTable.Columns.Add(attribute.LogicalName, type);
                                break;
                        }
                    }
                }
            }
            else
            {
                return null;
            }

            // Add the records in the datatable
            foreach (Entity record in this.entity.Entities)
            {
                DataRow row = dataTable.NewRow();
                foreach (DataColumn column in dataTable.Columns)
                {
                    if (record.Attributes.ContainsKey(column.ColumnName))
                    {
                        row[column.ColumnName] = record.Attributes[column.ColumnName];
                    }
                }
                dataTable.Rows.Add(row);
            }
            return dataTable;
        }

        /// TODO: injest an option set value and transform it to it's options set label
        /// 
        internal string OptionsetValueToLabelName()
        {
            return "";
        }

        /// <summary>
        /// A static method to convert AttributeTypeCode to System.Type
        /// </summary>
        /// <param name="attributeType"></param>
        /// <returns></returns>
        internal static Type ToSystemType(AttributeTypeCode? attributeType)
        {
            if (!attributeType.HasValue)
                return typeof(object);

            switch (attributeType.Value)
            {
                case AttributeTypeCode.String:
                    return typeof(string);

                case AttributeTypeCode.Memo:
                    return typeof(string);

                case AttributeTypeCode.Boolean:
                    return typeof(bool);

                case AttributeTypeCode.Integer:
                    return typeof(int);

                case AttributeTypeCode.Decimal:
                    return typeof(decimal);

                case AttributeTypeCode.Double:
                    return typeof(double);

                case AttributeTypeCode.Money:
                    return typeof(Money);

                case AttributeTypeCode.DateTime:
                    return typeof(DateTime);

                case AttributeTypeCode.Lookup:
                case AttributeTypeCode.Customer:
                case AttributeTypeCode.Owner:
                    return typeof(string);

                case AttributeTypeCode.Uniqueidentifier:
                    return typeof(Guid);

                case AttributeTypeCode.Virtual:
                    return typeof(object); // varies, depends on the virtual attribute

                default:
                    return typeof(object); // fallback for unsupported types
            }
        }

        /// <summary>
        /// A getter for the DataTable object.
        /// </summary>
        internal DataTable GetDataTable()
        {
            return this.dataTable;
        }

        

        /// <summary>
        /// Binds a <see cref="DataTable"/> to a <see cref="DataGridView"/> and configures the column headers for better
        /// readability.
        /// </summary>
        /// <remarks>This method sets the <see cref="DataGridView.DataSource"/> property to the provided <paramref
        /// name="dataTable"/>  and adjusts the column headers to use more user-friendly names for specific columns.  If the
        /// <paramref name="dataTable"/> is null or contains no rows, the method displays a message box to inform the
        /// user.</remarks>
        /// <param name="dataGridView">The <see cref="DataGridView"/> to which the data will be bound. Cannot be null.</param>
        /// <param name="dataTable">The <see cref="DataTable"/> containing the data to bind. If null or empty, a message box will be displayed
        /// indicating no data is available.</param>
        internal static void bindTable(DataGridView dataGridView, DataTable dataTable)
        {
            if (dataTable != null && dataTable.Rows.Count > 0)
            {
                dataGridView.DataSource = dataTable;

                // Set the column headers to be more user-friendly
                /*
                foreach (DataGridViewColumn column in dataGridView.Columns)
                {
                    switch (column.Name)
                    {
                        case "msdyn_flow_approvalid":
                            column.HeaderText = "Approval ID";
                            break;
                        case "msdyn_name":
                            column.HeaderText = "Approval Name";
                            break;
                        case "msdyn_flowname":
                            column.HeaderText = "Flow Name";
                            break;
                        case "msdyn_status":
                            column.HeaderText = "Status";
                            break;
                        default:
                            break;
                    }
                }*/

                dataGridView.AutoResizeColumns(DataGridViewAutoSizeColumnsMode.AllCells);
            }
            else
            {
                MessageBox.Show("No data available to bind to the DataGridView.");
            }
        }

        /// <summary>
        /// static method to convert AttributeMetadata to AttributeCollection
        /// </summary>
        /// <param name="attributeMetadatas"></param>
        /// <returns></returns>
        internal static AttributeCollection AttributeMetadataToAttributeCollection(IEnumerable<AttributeMetadata> attributeMetadatas)
        {
            var attributeCollection = new AttributeCollection();
            if (attributeMetadatas == null)
                return attributeCollection;

            foreach (var metadata in attributeMetadatas)
            {
                // Use logical name as key, and set value to null (or default) since metadata does not contain values
                attributeCollection.Add(metadata.LogicalName, null);
            }
            return attributeCollection;
        }

        /// <summary>
        /// This method converts an Entity Collection object to a simple DataTable object.
        /// Which indexes the display name of the columns in lieu of the attribute name as well as give easier readability of the data.
        /// </summary>
        /// 
        //For review: This method is used to convert an Entity Collection object to a simple DataTable object.
        //TODO: review AI slop. DO NOT USE IN CURRENT STATE.
        private DataTable GetSimpleDataTableFromEntityCollection()
        {
            // Init datatable
            DataTable simpleDataTable = new DataTable("Entities");
            //grab first entity to get attributes
            Microsoft.Xrm.Sdk.AttributeCollection attributes = this.entity.Entities.First().Attributes;
            // Add columns to the DataTable based on the attributes of the first entity
            if (this.entity.Entities.Count > 0)
            {
                foreach (KeyValuePair<string, object> attribute in attributes)
                {
                    simpleDataTable.Columns.Add(attribute.Key, attribute.Value.GetType());
                }
            }
            else
            {
                return null;
            }
            // Add the records in the datatable
            foreach (Entity record in this.entity.Entities)
            {
                DataRow row = simpleDataTable.NewRow();
                foreach (KeyValuePair<string, object> attribute in record.Attributes)
                {
                    row[attribute.Key] = attribute.Value;
                }
                simpleDataTable.Rows.Add(row);
            }
            return simpleDataTable;
        }
    }


}
