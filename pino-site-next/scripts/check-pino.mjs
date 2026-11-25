import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import { spawnSync } from 'node:child_process';

const siteDir = path.resolve(import.meta.dirname, '..');
const updateMode = process.argv.includes('--update');

function normalizeNewlines(text) {
  return text.replace(/\r\n/g, '\n');
}

function indentLines(text) {
  return text
    .split('\n')
    .map((line) => `  ${line}`)
    .join('\n');
}

function toPosixRel(absPath) {
  return path.relative(siteDir, absPath).split(path.sep).join('/');
}

function resolvePinoBin() {
  if (process.env.PINO_BIN) {
    return process.env.PINO_BIN;
  }

  const build = spawnSync(
    'dotnet',
    ['build', '../pino-csharp-re/Pino.Cli', '-c', 'Debug', '-v', 'q', '--nologo'],
    { cwd: siteDir, encoding: 'utf8' }
  );

  if (build.error) {
    console.error(build.error.message);
    process.exit(1);
  }
  if (build.status !== 0) {
    if (build.stdout) process.stdout.write(build.stdout);
    if (build.stderr) process.stderr.write(build.stderr);
    process.exit(1);
  }

  const exeName = process.platform === 'win32' ? 'pino.exe' : 'pino';
  return path.resolve(siteDir, '../pino-csharp-re/Pino.Cli/bin/Debug/net10.0', exeName);
}

function walkSorted(dir) {
  if (!fs.existsSync(dir)) return [];
  const results = [];
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) {
      results.push(...walkSorted(full));
    } else if (entry.isFile()) {
      results.push(full);
    }
  }
  return results.sort((a, b) => toPosixRel(a).localeCompare(toPosixRel(b)));
}

function withTempMain(content, fn) {
  const tmpDir = fs.mkdtempSync(path.join(os.tmpdir(), 'pino-check-'));
  try {
    fs.writeFileSync(path.join(tmpDir, 'main.pino'), content, 'utf8');
    return fn(tmpDir);
  } finally {
    fs.rmSync(tmpDir, { recursive: true, force: true });
  }
}

function fail(label, expected, actual) {
  console.log(`FAIL ${label}`);
  console.log('  expected:');
  console.log(indentLines(expected));
  console.log('  actual:');
  console.log(indentLines(actual));
}

function checkPassSnippet(pinoBin, absPath) {
  const rel = toPosixRel(absPath);
  const checkRes = spawnSync(pinoBin, ['check', absPath], {
    cwd: siteDir,
    encoding: 'utf8',
  });
  if (checkRes.error || checkRes.status !== 0) {
    const actual = normalizeNewlines((checkRes.stdout ?? '') + (checkRes.stderr ?? '')).trimEnd();
    fail(rel, 'exit 0', actual || `exit ${checkRes.status}`);
    return false;
  }

  const outPath = absPath.replace(/\.pino$/, '.out.txt');
  if (fs.existsSync(outPath)) {
    const runRes = spawnSync(pinoBin, ['run', absPath], {
      cwd: siteDir,
      encoding: 'utf8',
    });
    const actual = normalizeNewlines(runRes.stdout ?? '');
    if (runRes.error || runRes.status !== 0) {
      const errOut = normalizeNewlines((runRes.stdout ?? '') + (runRes.stderr ?? '')).trimEnd();
      fail(rel, 'exit 0', errOut || `exit ${runRes.status}`);
      return false;
    }
    const expected = normalizeNewlines(fs.readFileSync(outPath, 'utf8'));
    if (actual !== expected) {
      if (updateMode) {
        fs.writeFileSync(outPath, actual, 'utf8');
        console.log(`updated ${toPosixRel(outPath)}`);
      } else {
        fail(rel, expected.trimEnd(), actual.trimEnd());
        return false;
      }
    }
  }

  console.log(`ok   ${rel}`);
  return true;
}

function checkFailSnippet(pinoBin, absPath) {
  const rel = toPosixRel(absPath);
  const content = fs.readFileSync(absPath, 'utf8');
  const res = withTempMain(content, (tmpDir) =>
    spawnSync(pinoBin, ['check', 'main.pino'], {
      cwd: tmpDir,
      encoding: 'utf8',
    })
  );

  if (res.error || res.status === 0) {
    fail(rel, 'non-zero exit', `exit ${res.status ?? 'error'}`);
    return false;
  }

  const actual = normalizeNewlines((res.stdout ?? '') + (res.stderr ?? ''));
  const failTxtPath = absPath.replace(/\.fail\.pino$/, '.fail.txt');

  if (!fs.existsSync(failTxtPath)) {
    if (updateMode) {
      fs.writeFileSync(failTxtPath, actual, 'utf8');
      console.log(`updated ${toPosixRel(failTxtPath)}`);
      console.log(`ok   ${rel}`);
      return true;
    }
    fail(rel, `<missing ${toPosixRel(failTxtPath)}>`, actual.trimEnd());
    return false;
  }

  const expected = normalizeNewlines(fs.readFileSync(failTxtPath, 'utf8'));
  if (actual !== expected) {
    if (updateMode) {
      fs.writeFileSync(failTxtPath, actual, 'utf8');
      console.log(`updated ${toPosixRel(failTxtPath)}`);
    } else {
      fail(rel, expected.trimEnd(), actual.trimEnd());
      return false;
    }
  }

  console.log(`ok   ${rel}`);
  return true;
}

function extractPinoBlocks(markdown) {
  const lines = normalizeNewlines(markdown).split('\n');
  const blocks = [];
  let inBlock = false;
  let startLine = 0;
  let buffer = [];

  for (let i = 0; i < lines.length; i++) {
    const line = lines[i];
    if (!inBlock) {
      if (/^```pino\s*$/.test(line)) {
        inBlock = true;
        startLine = i + 1;
        buffer = [];
      }
    } else if (/^```\s*$/.test(line)) {
      blocks.push({ startLine, code: buffer.join('\n') + '\n' });
      inBlock = false;
    } else {
      buffer.push(line);
    }
  }
  return blocks;
}

function main() {
  const pinoBin = resolvePinoBin();
  let total = 0;
  let failures = 0;

  const snippetFiles = walkSorted(path.join(siteDir, 'src', 'snippets')).filter((f) =>
    f.endsWith('.pino')
  );

  for (const file of snippetFiles) {
    total++;
    const passed = file.endsWith('.fail.pino')
      ? checkFailSnippet(pinoBin, file)
      : checkPassSnippet(pinoBin, file);
    if (!passed) failures++;
  }

  const docFiles = walkSorted(path.join(siteDir, 'src')).filter(
    (f) => f.endsWith('.md') || f.endsWith('.mdx')
  );

  for (const docFile of docFiles) {
    const rel = toPosixRel(docFile);
    const blocks = extractPinoBlocks(fs.readFileSync(docFile, 'utf8'));
    for (const block of blocks) {
      total++;
      const label = `${rel}:${block.startLine}`;
      const res = withTempMain(block.code, (tmpDir) =>
        spawnSync(pinoBin, ['check', 'main.pino'], {
          cwd: tmpDir,
          encoding: 'utf8',
        })
      );
      if (res.error || res.status !== 0) {
        failures++;
        const actual = normalizeNewlines((res.stdout ?? '') + (res.stderr ?? '')).trimEnd();
        fail(label, 'exit 0', actual || `exit ${res.status}`);
      } else {
        console.log(`ok   ${label}`);
      }
    }
  }

  console.log(`checked ${total} snippet(s), ${failures} failure(s)`);
  process.exit(failures > 0 ? 1 : 0);
}

main();
