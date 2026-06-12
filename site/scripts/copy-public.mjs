import { cp, mkdir } from "node:fs/promises";
import { existsSync } from "node:fs";
import { join } from "node:path";

const source = join(process.cwd(), "site", "public");
const target = join(process.cwd(), "site", "dist", "public");

await mkdir(target, { recursive: true });
if (existsSync(source)) {
  await cp(source, target, { recursive: true });
}
