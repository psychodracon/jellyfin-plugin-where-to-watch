// Adds a release to manifest.json, with the checksum of the zip that was uploaded to the GitHub release.
// Usage: VERSION=1.0.0.0 REPO=owner/name ZIP=dist/file.zip node scripts/update-manifest.js
const crypto = require('crypto');
const fs = require('fs');
const path = require('path');

const { VERSION, REPO, ZIP } = process.env;
if (!VERSION || !REPO || !ZIP) {
    console.error('VERSION, REPO and ZIP must be set');
    process.exit(1);
}

if (!/^\d+\.\d+\.\d+\.\d+$/.test(VERSION)) {
    console.error(`Jellyfin needs a four-part version, got "${VERSION}"`);
    process.exit(1);
}

const root = path.join(__dirname, '..');
const manifestPath = path.join(root, 'manifest.json');
const buildYaml = fs.readFileSync(path.join(root, 'build.yaml'), 'utf8');
const targetAbi = /^targetAbi:\s*"?([\d.]+)"?/m.exec(buildYaml)?.[1];
if (!targetAbi) {
    console.error('targetAbi not found in build.yaml');
    process.exit(1);
}

const manifest = JSON.parse(fs.readFileSync(manifestPath, 'utf8'));
const plugin = manifest[0];
// The git tag may carry a leading "v" that the version does not.
const tag = process.env.TAG || VERSION;
const entry = {
    version: VERSION,
    changelog: process.env.CHANGELOG || `See the release notes: https://github.com/${REPO}/releases/tag/${tag}`,
    targetAbi,
    sourceUrl: `https://github.com/${REPO}/releases/download/${tag}/${path.basename(ZIP)}`,
    checksum: crypto.createHash('md5').update(fs.readFileSync(ZIP)).digest('hex'),
    timestamp: new Date().toISOString().replace(/\.\d{3}Z$/, 'Z'),
};

// Re-running a release replaces its entry instead of adding a duplicate.
plugin.versions = [entry, ...(plugin.versions || []).filter((v) => v.version !== VERSION)];

fs.writeFileSync(manifestPath, JSON.stringify(manifest, null, 4) + '\n');
console.log(`manifest.json: ${VERSION} (${entry.checksum})`);
