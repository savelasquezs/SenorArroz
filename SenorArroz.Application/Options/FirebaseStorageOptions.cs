namespace SenorArroz.Application.Options;

/// <summary>Opciones para subir archivos al bucket de Firebase / Google Cloud Storage (Fase Storage).</summary>
public class FirebaseStorageOptions
{
    public const string SectionName = "FirebaseStorage";

    /// <summary>Si es false, la API no usará Storage hasta activarlo (útil en entornos sin credencial).</summary>
    public bool Enabled { get; set; }

    /// <summary>Nombre del bucket, ej. <c>restaurante-railway.firebasestorage.app</c>.</summary>
    public string Bucket { get; set; } = string.Empty;

    /// <summary>Prefijo de objetos para logos de ticket por sucursal (sin / inicial ni final).</summary>
    public string BranchPrintPrefix { get; set; } = "branch-print";

    /// <summary>Prefijo de objetos para fotos de perfil (sin / inicial ni final).</summary>
    public string ProfilePrefix { get; set; } = "profile";

    /// <summary>
    /// Si es true, sube con ACL de objeto público. Con acceso uniforme (UBLA) puede fallar; usar false y lectura pública vía IAM del bucket.
    /// </summary>
    public bool UploadWithPublicReadAcl { get; set; } = true;

    /// <summary>
    /// Ruta al JSON (absoluta o relativa al directorio de trabajo). Opcional: preferir
    /// <c>GOOGLE_APPLICATION_CREDENTIALS_JSON_BASE64</c>, <c>GOOGLE_APPLICATION_CREDENTIALS_JSON</c> o <c>GOOGLE_APPLICATION_CREDENTIALS</c> (ruta).
    /// No commitear rutas aquí; usar User Secrets o variables de entorno.
    /// </summary>
    public string? GoogleApplicationCredentialsPath { get; set; }
}
