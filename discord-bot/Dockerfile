# Kis, gyors image felhős hoszthoz (Railway, Fly.io, vagy bármi más, ami
# Dockerfile-ból tud indítani). A HTTP port (PORT) és a Discord-adatok
# környezeti változóként jönnek — lásd .env.example.
FROM node:20-alpine

WORKDIR /app

COPY package.json package-lock.json* ./
RUN npm install --omit=dev

COPY src ./src

ENV NODE_ENV=production

# Csak dokumentáció — a tényleges portot a hoszt szolgáltatás állítja be
# a PORT környezeti változón keresztül.
EXPOSE 3000

CMD ["node", "src/index.js"]
