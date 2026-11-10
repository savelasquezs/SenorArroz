/**
 * Señor Arroz diagnostic gateway SDK. No dependencies and no credential storage.
 * Code Mode clients should call these typed methods, never execute arbitrary SQL.
 */
export type Scalar = string | number | boolean | null;
export type Filter = {
  column: string;
  op: "eq" | "ne" | "gt" | "gte" | "lt" | "lte";
  value: Scalar;
};
export type Query = {
  view: string;
  columns?: string[];
  filters?: Filter[];
  orderBy?: string;
  descending?: boolean;
  maxRows?: number;
};
export type Result = {
  columns: string[];
  rows: Record<string, string | null>[];
  truncated: boolean;
};
export type Schema = { views: Array<{ view: string; columns: string[] }> };

export class DataGateway {
  private readonly baseUrl: string;
  private readonly token: () => Promise<string> | string;
  private readonly fetchImpl: typeof fetch;

  constructor(
    baseUrl: string,
    token: () => Promise<string> | string,
    fetchImpl: typeof fetch = fetch
  ) {
    this.baseUrl = baseUrl;
    this.token = token;
    this.fetchImpl = fetchImpl;
    const url = new URL(baseUrl);
    if (url.username || url.password || url.search || url.hash ||
      (url.protocol !== "https:" && !(url.protocol === "http:" && ["localhost", "127.0.0.1", "[::1]"].includes(url.hostname))))
      throw new Error("HTTPS is required outside localhost");
  }

  private async request<T>(path: string, data?: Query): Promise<T> {
    const token = await this.token();
    if (!token) throw new Error("Missing authorization token");
    const controller = new AbortController();
    const timeout = setTimeout(() => controller.abort(), 6500);
    try {
      const response = await this.fetchImpl(
        this.baseUrl.replace(/\/$/, "") + "/api/internal/data-gateway/" + path,
        {
          method: data ? "POST" : "GET",
          headers: {
            Authorization: "Bearer " + token,
            ...(data ? { "Content-Type": "application/json" } : {})
          },
          body: data ? JSON.stringify(data) : undefined,
          redirect: "error",
          signal: controller.signal
        }
      );
      if (!response.ok) throw new Error("Gateway HTTP " + response.status);
      const reader = response.body?.getReader();
      if (!reader) throw new Error("Gateway returned an empty response");
      const chunks: Uint8Array[] = [];
      let size = 0;
      try {
        while (true) {
          const { done, value } = await reader.read();
          if (done) break;
          size += value.byteLength;
          if (size > 1048576) throw new Error("Gateway response exceeds limit");
          chunks.push(value);
        }
      } finally { await reader.cancel(); }
      const payload = new Uint8Array(size);
      let offset = 0;
      for (const chunk of chunks) { payload.set(chunk, offset); offset += chunk.byteLength; }
      return JSON.parse(new TextDecoder().decode(payload)) as T;
    } finally {
      clearTimeout(timeout);
    }
  }

  schema(): Promise<Schema> { return this.request("schema"); }
  query(request: Query): Promise<Result> { return this.request("query", request); }
  explain(request: Query): Promise<Result> { return this.request("explain", request); }
}

export type GatewayInvoker = <T>(tool: "get_schema" | "execute_query" | "explain_query", args: Query | Record<string, never>) => Promise<T>;

export function codeModeGateway(invoke: GatewayInvoker) {
  return Object.freeze({
    schema: () => invoke<Schema>("get_schema", {}),
    query: (request: Query) => invoke<Result>("execute_query", request),
    explain: (request: Query) => invoke<Result>("explain_query", request)
  });
}
