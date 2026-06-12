FROM node:20-slim AS deps
WORKDIR /app
COPY package.json package-lock.json* ./
RUN npm ci

FROM node:20-slim AS build
WORKDIR /app
COPY --from=deps /app/node_modules ./node_modules
COPY package.json tsconfig.json vitest.config.ts ./
COPY site ./site
RUN npm run build

FROM node:20-slim AS runtime
WORKDIR /app
ENV NODE_ENV=production
ENV PORT=8080
COPY package.json package-lock.json* ./
RUN npm ci --omit=dev
COPY --from=build /app/site/dist ./site/dist
EXPOSE 8080
CMD ["npm", "start"]
