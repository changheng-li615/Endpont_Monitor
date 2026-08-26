import { z } from "zod";

const boundedText = (maximum: number) => z.string().trim().min(1).max(maximum);
const nullableText = (maximum: number) =>
  z.string().trim().max(maximum).nullable().optional().transform((value) => value || null);
const timestamp = z.iso.datetime({ offset: true });

export const enrollmentSchema = z
  .object({
    installationId: z.uuid(),
    hostname: boundedText(255),
    windowsUser: nullableText(255),
    workEmail: z.email().max(320).nullable().optional().transform((value) => value || null),
    osVersion: boundedText(255),
    agentVersion: boundedText(64),
  })
  .strict();

export const heartbeatSchema = z
  .object({
    occurredAt: timestamp,
    agentVersion: boundedText(64),
    osVersion: boundedText(255),
    uptimeSeconds: z.number().int().nonnegative().max(Number.MAX_SAFE_INTEGER).nullable().optional(),
  })
  .strict();

export const currentProcessSchema = z
  .object({
    processName: boundedText(255),
    pid: z.number().int().nonnegative().max(2_147_483_647),
    executablePath: nullableText(2048),
    productVersion: nullableText(255),
    workingSetMb: z.number().finite().nonnegative().nullable().optional(),
    isForeground: z.boolean(),
  })
  .strict();

export const currentProcessesSchema = z
  .object({
    observedAt: timestamp,
    processes: z.array(currentProcessSchema).max(512),
  })
  .strict();

export const processEventSchema = z
  .object({
    occurredAt: timestamp,
    eventType: z.enum(["START", "STOP"]),
    processName: boundedText(255),
    pid: z.number().int().nonnegative().max(2_147_483_647),
    executablePath: nullableText(2048),
    productVersion: nullableText(255),
    workingSetMb: z.number().finite().nonnegative().nullable().optional(),
    isForeground: z.boolean().nullable().optional(),
  })
  .strict();

export const processEventsSchema = z
  .object({ events: z.array(processEventSchema).min(1).max(512) })
  .strict();

export const agentEventSchema = z
  .object({
    occurredAt: timestamp,
    eventType: boundedText(64),
    severity: z.enum(["INFO", "WARNING", "ERROR"]),
    message: boundedText(1000),
  })
  .strict();

export const agentEventsSchema = z
  .object({ events: z.array(agentEventSchema).min(1).max(100) })
  .strict();

export const screenshotMetadataSchema = z
  .object({
    capturedAt: timestamp,
    monitorIndex: z.coerce.number().int().min(1).max(64),
    width: z.coerce.number().int().positive().max(32768).nullable().optional(),
    height: z.coerce.number().int().positive().max(32768).nullable().optional(),
  })
  .strict();

export const uuidSchema = z.uuid();

export const monitoringPolicyResponseSchema = z
  .object({
    version: z.number().int().nonnegative(),
    monitoringEnabled: z.boolean(),
    screenshotEnabled: z.boolean(),
    screenshotIntervalSeconds: z.number().int().min(60).max(86400),
    processEnabled: z.boolean(),
    processIntervalSeconds: z.number().int().min(15).max(86400),
    timezone: boundedText(100),
    scheduleWindows: z.array(
      z
        .object({
          dayOfWeek: z.number().int().min(0).max(6),
          startLocalTime: z.string().regex(/^([01]\d|2[0-3]):[0-5]\d$/),
          endLocalTime: z.string().regex(/^([01]\d|2[0-3]):[0-5]\d$/),
        })
        .strict(),
    ),
  })
  .strict();
