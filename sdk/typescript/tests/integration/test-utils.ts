/**
 * Integration test utilities for MorphDB TypeScript SDK.
 *
 * These tests require a running MorphDB server.
 * Start the test server with: docker compose -f docker-compose.test.yml up -d
 */

import { randomUUID } from 'crypto';
import { MorphDBClient } from '../../src/client.js';

/**
 * Test server URL - defaults to docker-compose.test.yml exposed port
 */
export const MORPHDB_TEST_URL = process.env.MORPHDB_TEST_URL || 'http://localhost:5000';

/**
 * Creates a MorphDB client for integration testing
 */
export function createTestClient(tenantId?: string): MorphDBClient {
  return new MorphDBClient(MORPHDB_TEST_URL, {
    tenantId: tenantId || randomUUID(),
    timeout: 30000,
  });
}

/**
 * Generates a unique table name for test isolation
 */
export function uniqueTableName(): string {
  return `test_${randomUUID().slice(0, 8)}`;
}

/**
 * Helper to clean up a table after tests
 */
export async function cleanupTable(client: MorphDBClient, tableName: string): Promise<void> {
  try {
    await client.schema.dropTable(tableName);
  } catch {
    // Ignore errors during cleanup
  }
}

/**
 * Waits for a condition to be true (with timeout)
 */
export async function waitFor(
  condition: () => Promise<boolean>,
  timeoutMs: number = 5000,
  intervalMs: number = 100
): Promise<boolean> {
  const startTime = Date.now();

  while (Date.now() - startTime < timeoutMs) {
    if (await condition()) {
      return true;
    }
    await new Promise((resolve) => setTimeout(resolve, intervalMs));
  }

  return false;
}
