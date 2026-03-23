-- Ejecutar en PostgreSQL si no usas `dotnet ef database update`
ALTER TABLE branch ADD COLUMN IF NOT EXISTS latitude numeric(10,6) NULL;
ALTER TABLE branch ADD COLUMN IF NOT EXISTS longitude numeric(10,6) NULL;
