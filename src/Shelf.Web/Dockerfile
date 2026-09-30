# Build context is the repo root: docker build -f src/Shelf.Web/Dockerfile .
FROM node:22-alpine AS build
WORKDIR /web
COPY src/Shelf.Web/package.json src/Shelf.Web/package-lock.json ./
RUN npm ci
COPY src/Shelf.Web/ ./
RUN npm run build

# nginx serves the built files and forwards /api to the API container.
FROM nginx:alpine
COPY src/Shelf.Web/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /web/dist /usr/share/nginx/html
EXPOSE 80
