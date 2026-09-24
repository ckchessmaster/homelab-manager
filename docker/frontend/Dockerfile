# ==============================================================================
# Multi-Stage Dockerfile: ControlPlane Frontend (React 19 + Nginx)
# ==============================================================================

# Stage 1: Compile Vite & React 19 Assets
FROM node:22-alpine AS build
WORKDIR /app

# Install dependencies using lockfile
COPY src/frontend/package.json src/frontend/package-lock.json ./
RUN npm ci

# Copy frontend source files and run production build
COPY src/frontend/ ./
RUN npm run build

# Stage 2: Unprivileged Nginx Runtime
FROM nginxinc/nginx-unprivileged:alpine AS runtime

# Copy custom Nginx configuration with reverse proxy & WebSocket support
COPY docker/frontend/nginx.conf /etc/nginx/nginx.conf
COPY docker/frontend/40-configure-csp.sh /docker-entrypoint.d/40-configure-csp.sh

# Copy compiled static distribution into Nginx document root
COPY --from=build /app/dist /usr/share/nginx/html

EXPOSE 8080

# Health check probe against local Nginx endpoint
HEALTHCHECK --interval=15s --timeout=3s --start-period=5s --retries=3 \
    CMD wget -q --spider http://127.0.0.1:8080/ || exit 1

CMD ["nginx", "-g", "daemon off;"]
