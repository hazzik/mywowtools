﻿using System;
using System.ComponentModel;
using System.Data;
using System.IO;
using System.Text;
using System.Windows.Forms;
using System.Xml;
using System.Globalization;
using System.Linq;
using dbc2sql;

namespace DBC_Viewer
{
    public partial class MainForm : Form
    {
        DataTable m_dataTable;
        public DataTable DataTable { get { return m_dataTable; } }
        DataView m_dataView;
        public DataView DataView { get { return m_dataView; } }
        IWowClientDBReader m_reader;
        XmlDocument m_definitions;
        string DBCName;
        FilterForm m_filterForm;

        public MainForm()
        {
            InitializeComponent();
        }

        private void openToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (openFileDialog1.ShowDialog() != DialogResult.OK)
                return;

            dataGridView1.VirtualMode = false;
            dataGridView1.DataSource = null;

            toolStripProgressBar1.Visible = true;

            backgroundWorker1.RunWorkerAsync(openFileDialog1.FileName);
        }

        private void exitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Application.Exit();
        }

        private void dataGridView1_CellValueNeeded(object sender, DataGridViewCellValueEventArgs e)
        {
            if (e.RowIndex == -1)
                return;

            e.Value = m_dataView[e.RowIndex][e.ColumnIndex];
        }

        private void dataGridView1_CellToolTipTextNeeded(object sender, DataGridViewCellToolTipTextNeededEventArgs e)
        {
            if (e.RowIndex == -1)
                return;

            int val = 0;

            if (m_dataTable.Columns[e.ColumnIndex].DataType != typeof(string))
            {
                if(m_dataTable.Columns[e.ColumnIndex].DataType == typeof(int))
                    val = Convert.ToInt32(m_dataView[e.RowIndex][e.ColumnIndex]);
                else
                    val = (int)Convert.ToUInt32(m_dataView[e.RowIndex][e.ColumnIndex]);
            }
            else
                val = (from k in m_reader.StringTable where string.Compare(k.Value, (string)m_dataView[e.RowIndex][e.ColumnIndex], true) == 0 select k.Key).FirstOrDefault();

            var sb = new StringBuilder();
            sb.AppendFormat(new BinaryFormatter(), "Integer: {0:D}{1}", val, Environment.NewLine);
            sb.AppendFormat(new BinaryFormatter(), "HEX: {0:X}{1}", val, Environment.NewLine);
            sb.AppendFormat(new BinaryFormatter(), "BIN: {0:B}{1}", val, Environment.NewLine);
            sb.AppendFormat(new BinaryFormatter(), "Float: {0}{1}", BitConverter.ToSingle(BitConverter.GetBytes(val), 0), Environment.NewLine);
            sb.AppendFormat(new BinaryFormatter(), "String: {0}{1}", m_reader.StringTable[val], Environment.NewLine);
            e.ToolTipText = sb.ToString();
        }

        private void dataGridView1_CurrentCellChanged(object sender, EventArgs e)
        {
            if (dataGridView1.CurrentCell != null)
                label1.Text = String.Format("Current Cell: {0}x{1}", dataGridView1.CurrentCell.RowIndex, dataGridView1.CurrentCell.ColumnIndex);
        }

        private void dataGridView1_ColumnHeaderMouseClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            var col = dataGridView1.Columns[e.ColumnIndex].Name;

            if (dataGridView1.Columns[e.ColumnIndex].HeaderCell.SortGlyphDirection != SortOrder.Ascending)
                m_dataView.Sort = String.Format("[{0}] asc", col);
            else
                m_dataView.Sort = String.Format("[{0}] desc", col);

            SetDataView(m_dataView);
        }

        private void backgroundWorker1_DoWork(object sender, DoWorkEventArgs e)
        {
            var file = (string)e.Argument;

            LoadDefinitions();

            if (Path.GetExtension(file).ToLowerInvariant() == ".dbc")
                m_reader = new DBCReader(file);
            else
                m_reader = new DB2Reader(file);

            DBCName = Path.GetFileNameWithoutExtension(file);

            XmlElement definition = m_definitions["DBFilesClient"][DBCName];

            if (definition == null)
            {
                MessageBox.Show(DBCName + " missing definition!");
                e.Cancel = true;
                return;
            }

            XmlNodeList fields = definition.GetElementsByTagName("field");
            XmlNodeList indexes = definition.GetElementsByTagName("index");

            m_dataTable = new DataTable(Path.GetFileName(file));

            // Add columns
            CreateColumns(fields);

            CreateIndexes(indexes);

            // Add rows
            for (var i = 0; i < m_reader.RecordsCount; ++i)
            {
                var br = m_reader[i];

                var dataRow = m_dataTable.NewRow();

                // Add cells
                for (var j = 0; j < m_reader.FieldsCount; ++j)
                {
                    var colName = m_dataTable.Columns[j].ColumnName;

                    switch(fields[j].Attributes["type"].Value)
                    {
                        case "long":
                            dataRow[colName] = br.ReadInt64();
                            break;
                        case "ulong":
                            dataRow[colName] = br.ReadUInt64();
                            break;
                        case "int":
                            dataRow[colName] = br.ReadInt32();
                            break;
                        case "uint":
                            dataRow[colName] = br.ReadUInt32();
                            break;
                        case "short":
                            dataRow[colName] = br.ReadInt16();
                            break;
                        case "ushort":
                            dataRow[colName] = br.ReadUInt16();
                            break;
                        case "sbyte":
                            dataRow[colName] = br.ReadSByte();
                            break;
                        case "byte":
                            dataRow[colName] = br.ReadByte();
                            break;
                        case "float":
                            dataRow[colName] = br.ReadSingle();//.ToString(CultureInfo.InvariantCulture);
                            break;
                        case "double":
                            dataRow[colName] = br.ReadDouble();//.ToString(CultureInfo.InvariantCulture);
                            break;
                        case "string":
                            dataRow[colName] = m_reader.StringTable[br.ReadInt32()];
                            break;
                        default:
                            throw new Exception(String.Format("Unknown field type {0}!", fields[j].Attributes["type"].Value));
                    }
                }

                m_dataTable.Rows.Add(dataRow);

                int percent = (int)((float)m_dataTable.Rows.Count / (float)m_reader.RecordsCount * 100.0f);
                (sender as BackgroundWorker).ReportProgress(percent);
            }
        }

        private void CreateIndexes(XmlNodeList indexes)
        {
            var columns = new DataColumn[indexes.Count];
            var idx = 0;
            foreach (XmlElement index in indexes)
            {
                columns[idx++] = m_dataTable.Columns[index["primary"].InnerText];
            }
            m_dataTable.PrimaryKey = columns;
        }

        private void CreateColumns(XmlNodeList fields)
        {
            foreach (XmlElement field in fields)
            {
                var colName = field.Attributes["name"].Value;

                switch (field.Attributes["type"].Value)
                {
                    case "long":
                        m_dataTable.Columns.Add(colName, typeof(long));
                        break;
                    case "ulong":
                        m_dataTable.Columns.Add(colName, typeof(ulong));
                        break;
                    case "int":
                        m_dataTable.Columns.Add(colName, typeof(int));
                        break;
                    case "uint":
                        m_dataTable.Columns.Add(colName, typeof(uint));
                        break;
                    case "short":
                        m_dataTable.Columns.Add(colName, typeof(short));
                        break;
                    case "ushort":
                        m_dataTable.Columns.Add(colName, typeof(ushort));
                        break;
                    case "sbyte":
                        m_dataTable.Columns.Add(colName, typeof(sbyte));
                        break;
                    case "byte":
                        m_dataTable.Columns.Add(colName, typeof(byte));
                        break;
                    case "float":
                        m_dataTable.Columns.Add(colName, typeof(float));
                        break;
                    case "double":
                        m_dataTable.Columns.Add(colName, typeof(double));
                        break;
                    case "string":
                        m_dataTable.Columns.Add(colName, typeof(string));
                        break;
                    default:
                        throw new Exception(String.Format("Unknown field type {0}!", field.Attributes["type"].Value));
                }
            }
        }

        private void backgroundWorker1_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            toolStripProgressBar1.Value = e.ProgressPercentage;
        }

        private void backgroundWorker1_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            toolStripProgressBar1.Visible = false;
            toolStripProgressBar1.Value = 0;

            if (e.Cancelled == true)
                return;

            SetDataView(m_dataTable.DefaultView);
            dataGridView1.VirtualMode = true;
        }

        private void filterToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (m_dataTable == null)
                return;

            if (m_filterForm == null || m_filterForm.IsDisposed)
                m_filterForm = new FilterForm();

            if (!m_filterForm.Visible)
                m_filterForm.Show(this);
        }

        private void resetFilterToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (m_dataTable == null)
                return;

            SetDataView(m_dataTable.DefaultView);
        }

        public void SetDataView(DataView dataView)
        {
            m_dataView = dataView;
            dataGridView1.DataSource = m_dataView;
        }

        void LoadDefinitions()
        {
            m_definitions = new XmlDocument();
            m_definitions.Load("dbclayout.xml");
        }
    }
}