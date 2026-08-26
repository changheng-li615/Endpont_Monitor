import {
  createHash,
  randomBytes,
  scrypt as scryptCallback,
  timingSafeEqual,
} from "node:crypto";
import { promisify } from "node:util";

const scrypt = promisify(scryptCallback);
const SECRET_BYTES = 32;
const SALT_BYTES = 16;
const HASH_BYTES = 32;
const HASH_PREFIX = "scrypt-v1";

export function createDeviceSecret(): string {
  return randomBytes(SECRET_BYTES).toString("base64url");
}

export async function hashDeviceSecret(secret: string): Promise<string> {
  const salt = randomBytes(SALT_BYTES);
  const hash = (await scrypt(secret, salt, HASH_BYTES)) as Buffer;
  return `${HASH_PREFIX}$${salt.toString("base64url")}$${hash.toString("base64url")}`;
}

export async function verifyDeviceSecret(
  secret: string,
  encodedHash: string,
): Promise<boolean> {
  const [prefix, encodedSalt, encodedExpected, extra] = encodedHash.split("$");
  if (
    prefix !== HASH_PREFIX ||
    !encodedSalt ||
    !encodedExpected ||
    extra !== undefined ||
    secret.length > 1024
  ) {
    return false;
  }

  try {
    const salt = Buffer.from(encodedSalt, "base64url");
    const expected = Buffer.from(encodedExpected, "base64url");
    if (salt.length !== SALT_BYTES || expected.length !== HASH_BYTES) {
      return false;
    }
    const actual = (await scrypt(secret, salt, expected.length)) as Buffer;
    return timingSafeEqual(actual, expected);
  } catch {
    return false;
  }
}

export function secretsEqual(left: string, right: string): boolean {
  const leftDigest = createHash("sha256").update(left, "utf8").digest();
  const rightDigest = createHash("sha256").update(right, "utf8").digest();
  return timingSafeEqual(leftDigest, rightDigest);
}

export function readBearerToken(request: Request): string | null {
  const value = request.headers.get("authorization");
  if (!value) {
    return null;
  }
  const match = /^Bearer ([^\s]+)$/i.exec(value);
  return match?.[1] ?? null;
}

export function sha256Hex(value: Buffer | string): string {
  return createHash("sha256").update(value).digest("hex");
}
