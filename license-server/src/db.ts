import { Pool } from "pg";

const pool = new Pool({
  connectionString: process.env.DATABASE_URL,
  ssl: process.env.DATABASE_URL ? { rejectUnauthorized: false } : undefined,
});

export interface LicenseRow {
  key: string;
  licensee: string;
  email: string;
  tier: "free" | "pro" | "enterprise";
  max_entities: number;
  issued_at: Date;
  expires_at: Date;
  revoked: boolean;
  machine_fingerprint?: string;
}

export async function initDb(): Promise<void> {
  // If no DATABASE_URL, run in-memory mode
  if (!process.env.DATABASE_URL) {
    console.log("No DATABASE_URL — using in-memory store");
    return;
  }

  const client = await pool.connect();
  try {
    await client.query(`
      CREATE TABLE IF NOT EXISTS licenses (
        key TEXT PRIMARY KEY,
        licensee TEXT NOT NULL,
        email TEXT NOT NULL,
        tier TEXT NOT NULL DEFAULT 'pro',
        max_entities INTEGER NOT NULL DEFAULT 99,
        issued_at TIMESTAMP NOT NULL DEFAULT NOW(),
        expires_at TIMESTAMP NOT NULL,
        revoked BOOLEAN NOT NULL DEFAULT FALSE,
        machine_fingerprint TEXT
      )
    `);
    console.log("Database initialized");
  } finally {
    client.release();
  }
}

export async function insertLicense(license: Omit<LicenseRow, "issued_at" | "revoked">): Promise<void> {
  if (!process.env.DATABASE_URL) return;
  await pool.query(
    `INSERT INTO licenses (key, licensee, email, tier, max_entities, expires_at)
     VALUES ($1, $2, $3, $4, $5, $6)`,
    [license.key, license.licensee, license.email, license.tier, license.max_entities, license.expires_at]
  );
}

export async function findLicense(key: string): Promise<LicenseRow | null> {
  if (!process.env.DATABASE_URL) return null;
  const result = await pool.query("SELECT * FROM licenses WHERE key = $1", [key]);
  return result.rows[0] as LicenseRow | undefined ?? null;
}

export async function updateMachineFingerprint(key: string, fingerprint: string): Promise<void> {
  if (!process.env.DATABASE_URL) return;
  await pool.query("UPDATE licenses SET machine_fingerprint = $1 WHERE key = $2", [fingerprint, key]);
}

export async function revokeLicense(key: string): Promise<void> {
  if (!process.env.DATABASE_URL) return;
  await pool.query("UPDATE licenses SET revoked = TRUE WHERE key = $1", [key]);
}

export async function closeDb(): Promise<void> {
  if (!process.env.DATABASE_URL) return;
  await pool.end();
}
