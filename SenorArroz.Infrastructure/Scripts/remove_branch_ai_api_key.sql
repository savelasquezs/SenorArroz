-- Las credenciales de IA se leen exclusivamente de OPENAI_API_KEY / GEMINI_API_KEY.
-- Elimina cualquier secreto histórico persistido en la base de datos.
ALTER TABLE branch_ai_setting
    DROP COLUMN IF EXISTS api_key;
