﻿using System;
using System.Collections.Generic;
using System.Threading;

namespace BLToolkit.Data.Sql
{
	using Mapping;
	using Reflection.Extension;

	public class SqlTable : ISqlTableSource
	{
		#region Init

		public SqlTable() : this(Map.DefaultSchema)
		{
		}

		public SqlTable(MappingSchema mappingSchema)
		{
			_sourceID = Interlocked.Increment(ref SqlQuery.SourceIDCounter);

			if (mappingSchema == null) throw new ArgumentNullException("mappingSchema");

			_mappingSchema = mappingSchema;

			_fields = new ChildContainer<ISqlTableSource,SqlField>(this);
		}

		#endregion

		#region Init from type

		public SqlTable(MappingSchema mappingSchema, Type objectType)
			: this(mappingSchema)
		{
			bool isSet;
			_database = _mappingSchema.MetadataProvider.GetDatabaseName(objectType, _mappingSchema.Extensions, out isSet);
			_owner    = _mappingSchema.MetadataProvider.GetOwnerName   (objectType, _mappingSchema.Extensions, out isSet);
			_name     = _mappingSchema.MetadataProvider.GetTableName   (objectType, _mappingSchema.Extensions, out isSet);

			TypeExtension typeExt = TypeExtension.GetTypeExtension(objectType, _mappingSchema.Extensions);

			foreach (MemberMapper mm in MappingSchema.GetObjectMapper(objectType))
				if (mm.MapMemberInfo.SqlIgnore == false)
				{
					int order = _mappingSchema.MetadataProvider.GetPrimaryKeyOrder(objectType, typeExt, mm.MemberAccessor, out isSet);
					Fields.Add(new SqlField(mm.MemberName, mm.Name, mm.MapMemberInfo.Nullable, isSet ? order : int.MinValue));
				}
		}

		public SqlTable(Type objectType)
			: this(Map.DefaultSchema, objectType)
		{
		}

		#endregion

		#region Init from Table

		public SqlTable(MappingSchema mappingSchema, SqlTable table)
			: this(mappingSchema)
		{
			_alias        = table._alias;
			_database     = table._database;
			_owner        = table._owner;
			_name         = table._name;
			_physicalName = table._physicalName;

			foreach (SqlField field in table.Fields.Values)
				Fields.Add(new SqlField(field));

			foreach (Join join in table.Joins)
				Joins.Add(join.Clone());
		}

		public SqlTable(SqlTable table)
			: this(table.MappingSchema, table)
		{
		}

		public SqlTable(SqlTable table, IEnumerable<SqlField> fields, IEnumerable<Join> joins)
			: this(table.MappingSchema)
		{
			_alias        = table._alias;
			_database     = table._database;
			_owner        = table._owner;
			_name         = table._name;
			_physicalName = table._physicalName;

			Fields.AddRange(fields);
			Joins. AddRange(joins);
		}

		#endregion

		#region Init from XML

		public SqlTable(ExtensionList extensions, string name)
			: this(Map.DefaultSchema, extensions, name)
		{
		}

		public SqlTable(MappingSchema mappingSchema, ExtensionList extensions, string name)
			: this(mappingSchema)
		{
			if (extensions == null) throw new ArgumentNullException("extensions");
			if (name       == null) throw new ArgumentNullException("name");

			TypeExtension te = extensions[name];

			if (te == TypeExtension.Null)
				throw new ArgumentException(string.Format("Table '{0}' not found.", name));

			_name         = te.Name;
			_alias        = (string)te.Attributes["Alias"].       Value;
			_database     = (string)te.Attributes["Database"].    Value;
			_owner        = (string)te.Attributes["Owner"].       Value;
			_physicalName = (string)te.Attributes["PhysicalName"].Value;

			foreach (MemberExtension me in te.Members)
				Fields.Add(new SqlField(me.Name, (string)me["MapField"].Value ?? (string)me["PhysicalName"].Value, (bool?)me["Nullable"].Value ?? false, -1));

			foreach (AttributeExtension ae in te.Attributes["Join"])
				Joins.Add(new Join(ae));

			string baseExtension = (string)te.Attributes["BaseExtension"].Value;

			if (!string.IsNullOrEmpty(baseExtension))
				InitFromBase(new SqlTable(mappingSchema, extensions, baseExtension));

			string baseTypeName = (string)te.Attributes["BaseType"].Value;

			if (!string.IsNullOrEmpty(baseTypeName))
				InitFromBase(new SqlTable(mappingSchema, Type.GetType(baseTypeName, true, true)));
		}

		void InitFromBase(SqlTable baseTable)
		{
			if (_alias        == null) _alias        = baseTable._alias;
			if (_database     == null) _database     = baseTable._database;
			if (_owner        == null) _owner        = baseTable._owner;
			if (_physicalName == null) _physicalName = baseTable._physicalName;

			foreach (SqlField field in baseTable.Fields.Values)
				if (!Fields.ContainsKey(field.Name))
					Fields.Add(new SqlField(field));

			foreach (Join join in baseTable.Joins)
				if (Joins.Find(delegate(Join j) { return j.TableName == join.TableName; }) == null)
					Joins.Add(join);
		}

		#endregion

		#region Overrides

		public override string ToString()
		{
			return string.IsNullOrEmpty(Alias) ? Name : Name + " as " + Alias;
		}

		#endregion

		#region Public Members

		public SqlField this[string fieldName]
		{
			get
			{
				SqlField field;
				Fields.TryGetValue(fieldName, out field);
				return field;
			}
		}

		private string _name;
		public  string  Name { get { return _name; } set { _name = value; } }

		private string _alias;
		public  string  Alias { get { return _alias; } set { _alias = value; } }

		private string _database;
		public  string  Database { get { return _database; } set { _database = value; } }

		private string _owner;
		public  string  Owner { get { return _owner; } set { _owner = value; } }

		private string _physicalName;
		public  string  PhysicalName { get { return _physicalName ?? _name; } set { _physicalName = value; } }

		readonly ChildContainer<ISqlTableSource,SqlField> _fields;
		public   ChildContainer<ISqlTableSource,SqlField>  Fields { get { return _fields; } }

		readonly List<Join> _joins = new List<Join>();
		public   List<Join>  Joins { get { return _joins; } }

		private  SqlField _all;
		public   SqlField  All
		{
			get
			{
				if (_all == null)
				{
					_all = new SqlField("*", "*", true, -1);
					((IChild<ISqlTableSource>)_all).Parent = this;
				}

				return _all;
			}
		}

		#endregion

		#region Protected Members

		readonly MappingSchema _mappingSchema;
		public   MappingSchema  MappingSchema
		{
			get { return _mappingSchema; }
		}

		#endregion

		#region ISqlTableSource Members

		readonly int _sourceID;
		public   int  SourceID { get { return _sourceID; } }

		#endregion

		#region ICloneableElement Members

		public ICloneableElement Clone(Dictionary<ICloneableElement, ICloneableElement> objectTree, Predicate<ICloneableElement> doClone)
		{
			if (!doClone(this))
				return this;

			ICloneableElement clone;

			if (!objectTree.TryGetValue(this, out clone))
			{
				SqlTable table = new SqlTable(_mappingSchema);

				table._name         = _name;
				table._alias        = _alias;
				table._database     = _database;
				table._owner        = _owner;
				table._physicalName = _physicalName;

				table._fields.Clear();

				foreach (KeyValuePair<string,SqlField> field in _fields)
				{
					SqlField fc = new SqlField(field.Value);

					objectTree.   Add(field.Value, fc);
					table._fields.Add(field.Key,   fc);
				}

				table._joins.AddRange(_joins.ConvertAll<Join>(delegate(Join j) { return j.Clone(); }));

				objectTree.Add(this, clone = table);
				objectTree.Add(All,  table.All);
			}

			return clone;
		}

		#endregion

		#region IQueryElement Members

		public QueryElementType ElementType { get { return QueryElementType.SqlTable; } }

		#endregion
	}
}
