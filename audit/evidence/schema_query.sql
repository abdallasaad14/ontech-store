SET NOCOUNT ON;
SELECT t.name tbl, c.name col, ty.name typ, c.max_length len, c.precision prec, c.is_nullable nul
FROM sys.columns c JOIN sys.tables t ON t.object_id=c.object_id JOIN sys.types ty ON ty.user_type_id=c.user_type_id
ORDER BY t.name, c.column_id;
SELECT OBJECT_NAME(f.parent_object_id) tbl, f.name fk, COL_NAME(fc.parent_object_id,fc.parent_column_id) col, OBJECT_NAME(f.referenced_object_id) ref, f.delete_referential_action_desc ondel
FROM sys.foreign_keys f JOIN sys.foreign_key_columns fc ON fc.constraint_object_id=f.object_id ORDER BY 1,2;
SELECT OBJECT_NAME(i.object_id) tbl, i.name idx, i.is_unique uq, i.filter_definition flt, STRING_AGG(COL_NAME(ic.object_id,ic.column_id),',') WITHIN GROUP (ORDER BY ic.key_ordinal) cols
FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id
WHERE OBJECTPROPERTY(i.object_id,'IsUserTable')=1 GROUP BY i.object_id,i.name,i.is_unique,i.filter_definition ORDER BY 1,2;
SELECT OBJECT_NAME(parent_object_id) tbl, name, definition FROM sys.check_constraints;
SELECT name, type_desc FROM sys.objects WHERE type IN ('P','V','TR','FN','IF','TF');
SELECT name, collation_name, is_read_committed_snapshot_on, snapshot_isolation_state_desc FROM sys.databases WHERE name='OnTechStore';
