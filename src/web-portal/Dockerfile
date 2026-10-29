# Build from this folder:  docker build -t localhost:5000/web-portal:1 src/web-portal

# Stage 1: compile Angular into static files
FROM node:24-alpine AS build
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci
COPY . .
RUN npx ng build --configuration production

# Stage 2: serve them with Nginx
FROM nginx:1.29-alpine
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist/web-portal/browser /usr/share/nginx/html
