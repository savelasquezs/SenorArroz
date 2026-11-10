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
  constructor(
    private readonly baseUrl: string,
    private readonly token: () => Promise<string> | string,
    private readonly fetchImpl: typeof fetch = fetch
  ) {
    if (!/^https:\/\//i.test(baseUrl) && !/^http:\/\/localhost(?::\d+)?(?:\/|$)/i.test(baseUrl))
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
          signal: controller.signal
        }
      );
      if (!response.ok) throw new Error("Gateway HTTP " + response.status);
      return (await response.json()) as T;
    } finally {
      clearTimeout(timeout);
    }
  }

  schema(): Promise<Schema> { return this.request("schema"); }
  query(request: Query): Promise<Result> { return this.request("query", request); }
  explain(request: Query): Promise<Result> { return this.request("explain", request); }
}
