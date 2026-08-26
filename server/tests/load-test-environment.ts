import { fileURLToPath } from "node:url";
import { config } from "dotenv";

const testEnvironmentPath = fileURLToPath(new URL("../.env.test", import.meta.url));

export function loadTestEnvironment(): void {
  const result = config({ path: testEnvironmentPath, override: true, quiet: true });
  if (result.error) {
    throw new Error(
      "The dedicated server test configuration is missing. Restore server/.env.test before running tests.",
    );
  }

  const value = process.env.DATABASE_URL;
  if (!value) {
    throw new Error("server/.env.test must define DATABASE_URL.");
  }

  const databaseUrl = new URL(value);
  const hostIsLoopback = new Set(["127.0.0.1", "localhost", "::1"]).has(
    databaseUrl.hostname.toLowerCase(),
  );
  const databaseName = databaseUrl.pathname.replace(/^\//, "").toLowerCase();
  if (!hostIsLoopback || !databaseName.endsWith("_test")) {
    throw new Error(
      "Server tests are restricted to a loopback PostgreSQL database whose name ends with _test.",
    );
  }
}
