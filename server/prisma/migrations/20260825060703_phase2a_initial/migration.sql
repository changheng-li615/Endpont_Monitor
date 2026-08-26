-- CreateEnum
CREATE TYPE "ProcessEventType" AS ENUM ('START', 'STOP');

-- CreateEnum
CREATE TYPE "AgentEventSeverity" AS ENUM ('INFO', 'WARNING', 'ERROR');

-- CreateEnum
CREATE TYPE "ActivTrakMode" AS ENUM ('DISABLED', 'FIXTURE', 'LIVE');

-- CreateEnum
CREATE TYPE "ActivTrakMappingStatus" AS ENUM ('MATCHED', 'UNMATCHED', 'AMBIGUOUS');

-- CreateTable
CREATE TABLE "Device" (
    "id" UUID NOT NULL,
    "installationId" UUID NOT NULL,
    "hostname" VARCHAR(255) NOT NULL,
    "windowsUser" VARCHAR(255),
    "workEmail" VARCHAR(320),
    "osVersion" VARCHAR(255) NOT NULL,
    "agentVersion" VARCHAR(64) NOT NULL,
    "enrolledAt" TIMESTAMPTZ(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "lastSeenAt" TIMESTAMPTZ(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "lastHeartbeatAt" TIMESTAMPTZ(3),
    "isRevoked" BOOLEAN NOT NULL DEFAULT false,
    "deviceSecretHash" VARCHAR(255) NOT NULL,
    "monitoringPolicyId" UUID,
    "createdAt" TIMESTAMPTZ(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "updatedAt" TIMESTAMPTZ(3) NOT NULL,

    CONSTRAINT "Device_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "DeviceHeartbeat" (
    "id" UUID NOT NULL,
    "deviceId" UUID NOT NULL,
    "occurredAt" TIMESTAMPTZ(3) NOT NULL,
    "agentVersion" VARCHAR(64) NOT NULL,
    "osVersion" VARCHAR(255) NOT NULL,
    "uptimeSeconds" BIGINT,
    "createdAt" TIMESTAMPTZ(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "DeviceHeartbeat_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "DeviceCurrentProcess" (
    "id" UUID NOT NULL,
    "deviceId" UUID NOT NULL,
    "processKey" VARCHAR(768) NOT NULL,
    "processName" VARCHAR(255) NOT NULL,
    "pid" INTEGER NOT NULL,
    "executablePath" VARCHAR(2048),
    "productVersion" VARCHAR(255),
    "workingSetMb" DOUBLE PRECISION,
    "isForeground" BOOLEAN NOT NULL,
    "observedAt" TIMESTAMPTZ(3) NOT NULL,

    CONSTRAINT "DeviceCurrentProcess_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "ProcessEvent" (
    "id" UUID NOT NULL,
    "deviceId" UUID NOT NULL,
    "occurredAt" TIMESTAMPTZ(3) NOT NULL,
    "eventType" "ProcessEventType" NOT NULL,
    "processName" VARCHAR(255) NOT NULL,
    "pid" INTEGER NOT NULL,
    "executablePath" VARCHAR(2048),
    "productVersion" VARCHAR(255),
    "workingSetMb" DOUBLE PRECISION,
    "isForeground" BOOLEAN,
    "createdAt" TIMESTAMPTZ(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "ProcessEvent_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "Screenshot" (
    "id" UUID NOT NULL,
    "deviceId" UUID NOT NULL,
    "capturedAt" TIMESTAMPTZ(3) NOT NULL,
    "monitorIndex" INTEGER NOT NULL,
    "storageKey" VARCHAR(1024) NOT NULL,
    "mimeType" VARCHAR(32) NOT NULL,
    "width" INTEGER,
    "height" INTEGER,
    "sizeBytes" INTEGER NOT NULL,
    "sha256" CHAR(64) NOT NULL,
    "createdAt" TIMESTAMPTZ(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "Screenshot_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "AgentEvent" (
    "id" UUID NOT NULL,
    "deviceId" UUID NOT NULL,
    "occurredAt" TIMESTAMPTZ(3) NOT NULL,
    "eventType" VARCHAR(64) NOT NULL,
    "severity" "AgentEventSeverity" NOT NULL,
    "message" VARCHAR(1000) NOT NULL,
    "createdAt" TIMESTAMPTZ(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "AgentEvent_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "MonitoringPolicy" (
    "id" UUID NOT NULL,
    "name" VARCHAR(255) NOT NULL,
    "monitoringEnabled" BOOLEAN NOT NULL DEFAULT false,
    "screenshotEnabled" BOOLEAN NOT NULL DEFAULT false,
    "screenshotIntervalSeconds" INTEGER NOT NULL DEFAULT 300,
    "processEnabled" BOOLEAN NOT NULL DEFAULT false,
    "processIntervalSeconds" INTEGER NOT NULL DEFAULT 60,
    "timezone" VARCHAR(100) NOT NULL DEFAULT 'UTC',
    "version" INTEGER NOT NULL DEFAULT 1,
    "createdAt" TIMESTAMPTZ(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "updatedAt" TIMESTAMPTZ(3) NOT NULL,

    CONSTRAINT "MonitoringPolicy_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "MonitoringScheduleWindow" (
    "id" UUID NOT NULL,
    "policyId" UUID NOT NULL,
    "dayOfWeek" INTEGER NOT NULL,
    "startLocalTime" VARCHAR(5) NOT NULL,
    "endLocalTime" VARCHAR(5) NOT NULL,

    CONSTRAINT "MonitoringScheduleWindow_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "ActivTrakIntegration" (
    "id" UUID NOT NULL,
    "mode" "ActivTrakMode" NOT NULL DEFAULT 'DISABLED',
    "enabled" BOOLEAN NOT NULL DEFAULT false,
    "accountLabel" VARCHAR(255),
    "webhookTokenHash" VARCHAR(255),
    "webhookTokenLastRotatedAt" TIMESTAMPTZ(3),
    "activConnectEnabled" BOOLEAN NOT NULL DEFAULT false,
    "createdAt" TIMESTAMPTZ(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "updatedAt" TIMESTAMPTZ(3) NOT NULL,

    CONSTRAINT "ActivTrakIntegration_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "ActivTrakAlarmEvent" (
    "id" UUID NOT NULL,
    "source" VARCHAR(32) NOT NULL DEFAULT 'ACTIVTRAK',
    "externalEventId" VARCHAR(255),
    "alarmName" VARCHAR(255) NOT NULL,
    "alarmType" VARCHAR(255),
    "occurredAt" TIMESTAMPTZ(3) NOT NULL,
    "userIdentifier" VARCHAR(320),
    "computerIdentifier" VARCHAR(255),
    "application" VARCHAR(255),
    "domain" VARCHAR(255),
    "actionSummary" VARCHAR(1000),
    "activtrakDeepLink" VARCHAR(2048),
    "receivedAt" TIMESTAMPTZ(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "mappingStatus" "ActivTrakMappingStatus" NOT NULL DEFAULT 'UNMATCHED',
    "mappedDeviceId" UUID,

    CONSTRAINT "ActivTrakAlarmEvent_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "AuditEvent" (
    "id" UUID NOT NULL,
    "actorIdentifier" VARCHAR(320) NOT NULL,
    "action" VARCHAR(100) NOT NULL,
    "targetType" VARCHAR(100) NOT NULL,
    "targetId" VARCHAR(255),
    "summary" VARCHAR(1000) NOT NULL,
    "occurredAt" TIMESTAMPTZ(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "AuditEvent_pkey" PRIMARY KEY ("id")
);

-- CreateIndex
CREATE UNIQUE INDEX "Device_installationId_key" ON "Device"("installationId");

-- CreateIndex
CREATE INDEX "Device_lastSeenAt_idx" ON "Device"("lastSeenAt");

-- CreateIndex
CREATE INDEX "Device_lastHeartbeatAt_idx" ON "Device"("lastHeartbeatAt");

-- CreateIndex
CREATE INDEX "Device_monitoringPolicyId_idx" ON "Device"("monitoringPolicyId");

-- CreateIndex
CREATE INDEX "DeviceHeartbeat_deviceId_occurredAt_idx" ON "DeviceHeartbeat"("deviceId", "occurredAt");

-- CreateIndex
CREATE INDEX "DeviceHeartbeat_occurredAt_idx" ON "DeviceHeartbeat"("occurredAt");

-- CreateIndex
CREATE INDEX "DeviceCurrentProcess_deviceId_processName_idx" ON "DeviceCurrentProcess"("deviceId", "processName");

-- CreateIndex
CREATE INDEX "DeviceCurrentProcess_observedAt_idx" ON "DeviceCurrentProcess"("observedAt");

-- CreateIndex
CREATE UNIQUE INDEX "DeviceCurrentProcess_deviceId_processKey_key" ON "DeviceCurrentProcess"("deviceId", "processKey");

-- CreateIndex
CREATE INDEX "ProcessEvent_deviceId_occurredAt_idx" ON "ProcessEvent"("deviceId", "occurredAt");

-- CreateIndex
CREATE INDEX "ProcessEvent_occurredAt_idx" ON "ProcessEvent"("occurredAt");

-- CreateIndex
CREATE UNIQUE INDEX "Screenshot_storageKey_key" ON "Screenshot"("storageKey");

-- CreateIndex
CREATE INDEX "Screenshot_deviceId_capturedAt_idx" ON "Screenshot"("deviceId", "capturedAt");

-- CreateIndex
CREATE INDEX "Screenshot_capturedAt_idx" ON "Screenshot"("capturedAt");

-- CreateIndex
CREATE INDEX "AgentEvent_deviceId_occurredAt_idx" ON "AgentEvent"("deviceId", "occurredAt");

-- CreateIndex
CREATE INDEX "AgentEvent_occurredAt_idx" ON "AgentEvent"("occurredAt");

-- CreateIndex
CREATE INDEX "MonitoringPolicy_updatedAt_idx" ON "MonitoringPolicy"("updatedAt");

-- CreateIndex
CREATE INDEX "MonitoringScheduleWindow_policyId_dayOfWeek_idx" ON "MonitoringScheduleWindow"("policyId", "dayOfWeek");

-- CreateIndex
CREATE INDEX "ActivTrakAlarmEvent_occurredAt_idx" ON "ActivTrakAlarmEvent"("occurredAt");

-- CreateIndex
CREATE INDEX "ActivTrakAlarmEvent_mappedDeviceId_idx" ON "ActivTrakAlarmEvent"("mappedDeviceId");

-- CreateIndex
CREATE INDEX "ActivTrakAlarmEvent_alarmName_idx" ON "ActivTrakAlarmEvent"("alarmName");

-- CreateIndex
CREATE INDEX "AuditEvent_occurredAt_idx" ON "AuditEvent"("occurredAt");

-- CreateIndex
CREATE INDEX "AuditEvent_action_idx" ON "AuditEvent"("action");

-- AddForeignKey
ALTER TABLE "Device" ADD CONSTRAINT "Device_monitoringPolicyId_fkey" FOREIGN KEY ("monitoringPolicyId") REFERENCES "MonitoringPolicy"("id") ON DELETE SET NULL ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "DeviceHeartbeat" ADD CONSTRAINT "DeviceHeartbeat_deviceId_fkey" FOREIGN KEY ("deviceId") REFERENCES "Device"("id") ON DELETE CASCADE ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "DeviceCurrentProcess" ADD CONSTRAINT "DeviceCurrentProcess_deviceId_fkey" FOREIGN KEY ("deviceId") REFERENCES "Device"("id") ON DELETE CASCADE ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "ProcessEvent" ADD CONSTRAINT "ProcessEvent_deviceId_fkey" FOREIGN KEY ("deviceId") REFERENCES "Device"("id") ON DELETE CASCADE ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "Screenshot" ADD CONSTRAINT "Screenshot_deviceId_fkey" FOREIGN KEY ("deviceId") REFERENCES "Device"("id") ON DELETE CASCADE ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "AgentEvent" ADD CONSTRAINT "AgentEvent_deviceId_fkey" FOREIGN KEY ("deviceId") REFERENCES "Device"("id") ON DELETE CASCADE ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "MonitoringScheduleWindow" ADD CONSTRAINT "MonitoringScheduleWindow_policyId_fkey" FOREIGN KEY ("policyId") REFERENCES "MonitoringPolicy"("id") ON DELETE CASCADE ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "ActivTrakAlarmEvent" ADD CONSTRAINT "ActivTrakAlarmEvent_mappedDeviceId_fkey" FOREIGN KEY ("mappedDeviceId") REFERENCES "Device"("id") ON DELETE SET NULL ON UPDATE CASCADE;
