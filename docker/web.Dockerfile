# syntax=docker/dockerfile:1

FROM node:22-alpine AS build
WORKDIR /app

# npm ci needs the lockfile; installing before copying sources keeps the
# dependency layer cached.
COPY web/package.json web/package-lock.json ./
RUN npm ci

COPY web/ ./
RUN npm run build

FROM nginx:1.27-alpine AS runtime

COPY docker/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist /usr/share/nginx/html

EXPOSE 80

HEALTHCHECK --interval=15s --timeout=3s --start-period=10s --retries=3 \
    CMD wget --quiet --spider http://localhost/ || exit 1
