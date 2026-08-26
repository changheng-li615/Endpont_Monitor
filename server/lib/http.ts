import { ZodError, type ZodType } from "zod";

export class HttpError extends Error {
  constructor(
    public readonly status: number,
    message: string,
  ) {
    super(message);
  }
}

export function jsonError(status: number, message: string): Response {
  return Response.json({ error: message }, { status });
}

export async function readBoundedJson<T>(
  request: Request,
  schema: ZodType<T>,
  maximumBytes = 256 * 1024,
): Promise<T> {
  const declaredLength = Number(request.headers.get("content-length") ?? "0");
  if (Number.isFinite(declaredLength) && declaredLength > maximumBytes) {
    throw new HttpError(413, "Request body is too large.");
  }
  const text = await request.text();
  if (Buffer.byteLength(text, "utf8") > maximumBytes) {
    throw new HttpError(413, "Request body is too large.");
  }
  let value: unknown;
  try {
    value = JSON.parse(text);
  } catch {
    throw new HttpError(400, "Request body must be valid JSON.");
  }
  try {
    return schema.parse(value);
  } catch (error) {
    if (error instanceof ZodError) {
      throw new HttpError(400, "Request body failed validation.");
    }
    throw error;
  }
}

export function routeError(error: unknown): Response {
  if (error instanceof HttpError) {
    return jsonError(error.status, error.message);
  }
  console.error("Request failed without exposing request credentials or payload.");
  return jsonError(500, "Internal server error.");
}
