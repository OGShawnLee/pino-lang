import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';

const __dirname = path.dirname(fileURLToPath(import.meta.url));

const source = path.resolve(__dirname, '../assets');
const destination = path.resolve(__dirname, 'src/assets');

if (fs.existsSync(source)) {
  if (!fs.existsSync(destination)) {
    fs.cpSync(source, destination, { recursive: true });
    console.log('Successfully synchronized assets to src/assets');
  } else {
    console.log('src/assets already exists. Skipping sync.');
  }
} else {
  console.warn('Source assets directory not found at:', source);
}
