-- Add optional client-generated identifiers without changing existing Phase 2A rows.
ALTER TABLE "ProcessEvent" ADD COLUMN "clientEventId" UUID;
ALTER TABLE "Screenshot" ADD COLUMN "captureId" UUID;
ALTER TABLE "AgentEvent" ADD COLUMN "clientEventId" UUID;

CREATE UNIQUE INDEX "ProcessEvent_deviceId_clientEventId_key"
ON "ProcessEvent"("deviceId", "clientEventId");

CREATE UNIQUE INDEX "Screenshot_deviceId_captureId_key"
ON "Screenshot"("deviceId", "captureId");

CREATE UNIQUE INDEX "AgentEvent_deviceId_clientEventId_key"
ON "AgentEvent"("deviceId", "clientEventId");
