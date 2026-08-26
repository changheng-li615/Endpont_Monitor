-- Database-level bounds complement API validation and protect direct administrative writes.
ALTER TABLE "DeviceHeartbeat"
  ADD CONSTRAINT "DeviceHeartbeat_uptimeSeconds_nonnegative" CHECK ("uptimeSeconds" IS NULL OR "uptimeSeconds" >= 0);

ALTER TABLE "DeviceCurrentProcess"
  ADD CONSTRAINT "DeviceCurrentProcess_pid_nonnegative" CHECK ("pid" >= 0),
  ADD CONSTRAINT "DeviceCurrentProcess_workingSetMb_nonnegative" CHECK ("workingSetMb" IS NULL OR "workingSetMb" >= 0);

ALTER TABLE "ProcessEvent"
  ADD CONSTRAINT "ProcessEvent_pid_nonnegative" CHECK ("pid" >= 0),
  ADD CONSTRAINT "ProcessEvent_workingSetMb_nonnegative" CHECK ("workingSetMb" IS NULL OR "workingSetMb" >= 0);

ALTER TABLE "Screenshot"
  ADD CONSTRAINT "Screenshot_monitorIndex_bounds" CHECK ("monitorIndex" BETWEEN 1 AND 64),
  ADD CONSTRAINT "Screenshot_dimensions_positive" CHECK (("width" IS NULL OR "width" > 0) AND ("height" IS NULL OR "height" > 0)),
  ADD CONSTRAINT "Screenshot_sizeBytes_positive" CHECK ("sizeBytes" > 0),
  ADD CONSTRAINT "Screenshot_sha256_hex" CHECK ("sha256" ~ '^[0-9a-f]{64}$'),
  ADD CONSTRAINT "Screenshot_mimeType_allowed" CHECK ("mimeType" IN ('image/png', 'image/jpeg'));

ALTER TABLE "MonitoringPolicy"
  ADD CONSTRAINT "MonitoringPolicy_screenshotInterval_bounds" CHECK ("screenshotIntervalSeconds" BETWEEN 60 AND 86400),
  ADD CONSTRAINT "MonitoringPolicy_processInterval_bounds" CHECK ("processIntervalSeconds" BETWEEN 15 AND 86400),
  ADD CONSTRAINT "MonitoringPolicy_version_positive" CHECK ("version" >= 1);

ALTER TABLE "MonitoringScheduleWindow"
  ADD CONSTRAINT "MonitoringScheduleWindow_dayOfWeek_bounds" CHECK ("dayOfWeek" BETWEEN 0 AND 6),
  ADD CONSTRAINT "MonitoringScheduleWindow_startLocalTime_format" CHECK ("startLocalTime" ~ '^([01][0-9]|2[0-3]):[0-5][0-9]$'),
  ADD CONSTRAINT "MonitoringScheduleWindow_endLocalTime_format" CHECK ("endLocalTime" ~ '^([01][0-9]|2[0-3]):[0-5][0-9]$'),
  ADD CONSTRAINT "MonitoringScheduleWindow_nonempty" CHECK ("startLocalTime" <> "endLocalTime");
