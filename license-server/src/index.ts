import express from "express";
import cors from "cors";
import crypto from "crypto";
import { v4 as uuid } from "uuid";
import { initDb, insertLicense, findLicense, updateMachineFingerprint, revokeLicense, closeDb, LicenseRow } from "./db";

const app = express();

const PORT = process.env.PORT ? parseInt(process.env.PORT) : 3000;
const ADMIN_TOKEN = process.env.ADMIN_TOKEN || "change-me-in-production";
const LS_WEBHOOK_SECRET = process.env.LEMON_SQUEEZY_WEBHOOK_SECRET;

// Raw body capture for webhook signature verification
app.use(express.json({
  verify: (req, _res, buf) => { (req as any).rawBody = buf; },
}));
app.use(cors());

// ─── In-memory fallback store ───
const memLicenses = new Map<string, LicenseRow>();

// Seed demo key
const DEMO_KEY = "ZMA-DEMO-0000-0000";
memLicenses.set(DEMO_KEY, {
  key: DEMO_KEY,
  licensee: "Demo User",
  email: "demo@example.com",
  tier: "pro",
  max_entities: 99,
  issued_at: new Date(),
  expires_at: new Date(Date.now() + 365 * 24 * 60 * 60 * 1000),
  revoked: false,
});

async function getLicense(key: string): Promise<LicenseRow | null> {
  const pg = await findLicense(key);
  if (pg) return pg;
  return memLicenses.get(key) ?? null;
}

async function saveLicense(lic: LicenseRow): Promise<void> {
  await insertLicense(lic);
  memLicenses.set(lic.key, lic);
}

// ─── POST /api/license/generate ───
app.post("/api/license/generate", async (req, res) => {
  const auth = req.headers.authorization;
  if (auth !== `Bearer ${ADMIN_TOKEN}`) {
    return res.status(401).json({ error: "Unauthorized" });
  }

  const { licensee, email, tier = "pro", days = 365, maxEntities = 99 } = req.body;
  if (!licensee || !email) {
    return res.status(400).json({ error: "licensee and email are required" });
  }

  const key = `ZMA-${uuid().split("-").slice(0, 4).join("-").toUpperCase()}`;

  const license: LicenseRow = {
    key,
    licensee,
    email,
    tier,
    max_entities: maxEntities,
    issued_at: new Date(),
    expires_at: new Date(Date.now() + days * 24 * 60 * 60 * 1000),
    revoked: false,
  };

  await saveLicense(license);
  return res.json({
    key,
    licensee,
    email,
    tier,
    maxEntities: license.max_entities,
    expiresAt: license.expires_at.toISOString(),
  });
});

// ─── POST /api/license/validate ───
app.post("/api/license/validate", async (req, res) => {
  const { key, machineFingerprint } = req.body;
  if (!key) {
    return res.status(400).json({ valid: false, error: "License key is required" });
  }

  const license = await getLicense(key);
  if (!license) {
    return res.json({ valid: false, error: "License key not found" });
  }

  if (license.revoked) {
    return res.json({ valid: false, error: "License key has been revoked" });
  }

  if (new Date(license.expires_at) < new Date()) {
    return res.json({ valid: false, error: "License key has expired" });
  }

  // Machine-lock on first activation
  if (machineFingerprint) {
    if (!license.machine_fingerprint) {
      await updateMachineFingerprint(key, machineFingerprint);
      license.machine_fingerprint = machineFingerprint;
    } else if (license.machine_fingerprint !== machineFingerprint) {
      return res.json({ valid: false, error: "License key already activated on another machine" });
    }
  }

  return res.json({
    valid: true,
    key: license.key,
    licensee: license.licensee,
    email: license.email,
    tier: license.tier,
    maxEntities: license.max_entities,
    expiresAt: license.expires_at.toISOString(),
  });
});

// ─── POST /api/license/revoke ───
app.post("/api/license/revoke", async (req, res) => {
  const auth = req.headers.authorization;
  if (auth !== `Bearer ${ADMIN_TOKEN}`) {
    return res.status(401).json({ error: "Unauthorized" });
  }

  const { key } = req.body;
  const license = await getLicense(key);
  if (!license) {
    return res.status(404).json({ error: "License key not found" });
  }

  await revokeLicense(key);
  license.revoked = true;
  return res.json({ revoked: true, key });
});

// ─── POST /api/license/webhook/lemonsqueezy ───
app.post("/api/license/webhook/lemonsqueezy", async (req, res) => {
  // Verify signature
  if (LS_WEBHOOK_SECRET) {
    const sig = req.headers["x-signature"] as string;
    if (!sig) return res.status(401).json({ error: "Missing signature" });
    const expected = crypto.createHmac("sha256", LS_WEBHOOK_SECRET).update((req as any).rawBody).digest("hex");
    if (sig !== expected) return res.status(401).json({ error: "Invalid signature" });
  }

  const event = req.body.meta?.event_name;
  if (event !== "order_created") return res.json({ received: true });

  const attrs = req.body.data?.attributes;
  const licensee = attrs?.customer_name || attrs?.user_name || "Customer";
  const email = attrs?.user_email;
  if (!email) return res.status(400).json({ error: "email required" });

  const key = `ZMA-${uuid().split("-").slice(0, 4).join("-").toUpperCase()}`;
  const license: LicenseRow = {
    key,
    licensee,
    email,
    tier: "pro",
    max_entities: 99,
    issued_at: new Date(),
    expires_at: new Date(Date.now() + 365 * 24 * 60 * 60 * 1000),
    revoked: false,
  };
  await saveLicense(license);
  console.log(`License ${key} generated for ${email}`);
  return res.json({ key, licensee, email, tier: "pro", maxEntities: 99 });
});

// ─── Health check ───
app.get("/health", async (_req, res) => {
  const pgOk = !process.env.DATABASE_URL || (await getLicense("") !== undefined) || true;
  res.json({ status: "ok", postgres: !!process.env.DATABASE_URL });
});

// ─── Start ───
async function main() {
  await initDb();
  app.listen(PORT, () => {
    console.log(`ZMA license server running on port ${PORT}`);
  });
}

main().catch((err) => {
  console.error("Failed to start:", err);
  process.exit(1);
});

process.on("SIGTERM", async () => {
  await closeDb();
  process.exit(0);
});
