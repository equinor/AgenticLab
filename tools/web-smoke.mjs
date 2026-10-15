import assert from "node:assert/strict";
import { mkdirSync, readFileSync } from "node:fs";
import { createRequire } from "node:module";
import { tmpdir } from "node:os";
import path from "node:path";

const { chromium } = createRequire(import.meta.url)("playwright");
const baseUrl = process.env.AGENTICLAB_URL ?? "http://127.0.0.1:5186";
const applicationVersion = readFileSync(new URL("../version.txt", import.meta.url), "utf8").trim();
const learnOnly = process.argv.includes("--learn-only");
const navigationOnly = process.argv.includes("--navigation-only");
const markdownOnly = process.argv.includes("--markdown-only");
const expectedHosts = process.env.AGENTICLAB_HOSTS?.split(",").map(key => key.trim());
const legacyHosts = JSON.parse(process.env.AGENTICLAB_HOST_ALIASES ?? "{}");
const screenshots = process.env.AGENTICLAB_SCREENSHOTS ?? path.join(tmpdir(), "agentic-lab-web-smoke");
mkdirSync(screenshots, { recursive: true });
const browser = await chromium.launch({ headless: true });
const errors = [];
const imageRequests = [];
let currentPage;

try {
    for (const viewport of [{ width: 1440, height: 1000 }, { width: 1024, height: 900 }, { width: 390, height: 844 }, { width: 1920, height: 1080 }]) {
        if (learnOnly && viewport.width < 900) continue;
        const context = await browser.newContext({ viewport });
        if (!markdownOnly && !learnOnly) {
            const teaserPage = currentPage = await context.newPage();
            teaserPage.setDefaultTimeout(15000);
            teaserPage.on("pageerror", error => errors.push(error.message));
            await checkTeaser(teaserPage, viewport);
            await teaserPage.close();
            const fallbackPage = currentPage = await browser.newPage({ viewport, javaScriptEnabled: false });
            const fallbackResponse = await fallbackPage.goto(new URL("/", baseUrl).href, { waitUntil: "networkidle" });
            await checkVersion(fallbackPage, fallbackResponse);
            assert.equal(await fallbackPage.locator("[data-teaser-slide]:visible").count(), 3, "All messages are readable without JavaScript");
            assert.equal(await fallbackPage.locator("[data-teaser-controls]").isVisible(), false, "Fallback has no inactive controls");
            assert.equal(await fallbackPage.locator(".home-purpose").isVisible(), true, "The purpose is readable without JavaScript");
            await capture(fallbackPage, `home-no-js-${viewport.width}`);
            await fallbackPage.close();
        }
        const page = currentPage = await context.newPage();
        page.setDefaultTimeout(15000);
        page.on("pageerror", error => errors.push(error.message));
        if (learnOnly) {
            page.on("request", request => {
                const url = new URL(request.url());
                assert.equal(url.origin, new URL(baseUrl).origin, "Guide requests stay on the local Web host");
                assert.doesNotMatch(url.pathname, /^\/(chat|api|discovery)(\/|$)/, "Guide checks do not invoke execution or discovery");
            });
            await checkLearning(page, viewport.width);
            await context.close();
            currentPage = null;
            console.log(`Agent guide ${viewport.width}x${viewport.height}: passed`);
            continue;
        }
        page.on("request", request => {
            if (new URL(request.url()).hostname === "images.invalid") imageRequests.push(request.url());
        });
        if (markdownOnly) {
            await page.goto(new URL("/design-system", baseUrl).href, { waitUntil: "networkidle" });
            await page.getByRole("button", { name: "New conversation", exact: true }).click();
            await page.waitForFunction(() => document.querySelector("section > output")?.textContent === "1 activations");
            await checkConversationMarkdown(page);
            await context.close();
            currentPage = null;
            console.log(`Conversation Markdown ${viewport.width}x${viewport.height}: passed`);
            continue;
        }
        await checkHome(page, viewport);
        await checkVersion(page);
        if (navigationOnly) {
            await checkLearning(page, viewport.width);
            await checkVersion(page);
            await page.goto(new URL("/design-system", baseUrl).href, { waitUntil: "networkidle" });
            assert.equal(await page.getByRole("link", { name: "Live flow", exact: true }).getAttribute("href"), "/flow");
            const missing = await page.goto(new URL("/missing-home-test", baseUrl).href, { waitUntil: "networkidle" });
            assert.equal(missing.status(), 404);
            await page.getByRole("heading", { name: "Page not found", exact: true }).waitFor();
            assert.equal(await page.getByRole("link", { name: "Live flow", exact: true }).getAttribute("href"), "/flow");
            await page.getByRole("link", { name: "Agentic Lab home", exact: true }).click();
            await page.locator(".home-app").waitFor();
            await checkVersion(page);
            await context.close();
            currentPage = null;
            console.log(`Home, navigation and Learn ${viewport.width}x${viewport.height}: passed`);
            continue;
        }
        await openFlow(page);
        await checkVersion(page);
        await checkHosts(page, viewport.width);
        await checkConversationHeader(page);
        await checkExecutionControls(page);
        await capture(page, `flow-${viewport.width}`);
        const layout = await page.evaluate(() => {
            const conversation = document.querySelector(".side-left").getBoundingClientRect();
            const flow = document.querySelector(".flow-main-col").getBoundingClientRect();
            return { conversationX: conversation.x, conversationY: conversation.y, flowX: flow.x, flowY: flow.y };
        });
        if (viewport.width >= 900) assert.ok(layout.flowX > layout.conversationX, "Desktop workspace must have two panes");
        else assert.ok(layout.flowY > layout.conversationY, "Narrow workspace must stack conversation before flow");

        await page.locator("#message").fill("Unsent layout test");
        await page.getByRole("tab", { name: /^Settings/ }).click();
        await page.locator("#settings-panel").waitFor({ state: "visible" });
        await page.getByRole("tab", { name: "Conversation", exact: true }).click();
        await page.locator("#message").waitFor({ state: "visible" });
        assert.equal(await page.locator("#message").inputValue(), "Unsent layout test");

        if (viewport.width === 1440) {
            const splitter = page.getByRole("separator", { name: "Resize the Conversation panel" });
            await splitter.focus();
            await page.keyboard.press("ArrowLeft");
            await page.waitForFunction(() => localStorage.getItem("agenticlab-panels")?.endsWith("|0"));
            const keyboardWidth = await page.evaluate(() => Number(localStorage.getItem("agenticlab-panels").split("|")[3]));
            const handle = await splitter.boundingBox();
            await page.mouse.move(handle.x + handle.width / 2, handle.y + handle.height / 2);
            await page.mouse.down();
            await page.mouse.move(handle.x + handle.width / 2 - 30, handle.y + handle.height / 2, { steps: 5 });
            await page.mouse.up();
            await page.waitForFunction(previous => Number(localStorage.getItem("agenticlab-panels")?.split("|")[3]) < previous, keyboardWidth);
            const stored = await page.evaluate(() => localStorage.getItem("agenticlab-panels"));
            assert.equal(stored.split("|").length, 7);
            await openFlow(page);
            assert.equal(await page.evaluate(() => localStorage.getItem("agenticlab-panels")), stored);
            await page.getByRole("tab", { name: /^Settings/ }).click();
            await page.getByRole("button", { name: "Reset layout", exact: true }).click();
            await page.waitForFunction(() => localStorage.getItem("agenticlab-panels")?.endsWith("|1"));
            await page.evaluate(() => localStorage.setItem("agenticlab-panels", "0|0|0|480|300|360"));
            await openFlow(page);
            const restored = await page.locator(".side-left").boundingBox();
            assert.ok(Math.abs(restored.width - 480) < 2, "Legacy conversation width must be restored");
            await page.evaluate(() => document.documentElement.style.zoom = "2");
            await capture(page, "flow-200-percent");
            await page.evaluate(() => document.documentElement.style.zoom = "");
            await page.evaluate(() => localStorage.setItem("agenticlab-panels", "0|0|0|240|300|360"));
            await openFlow(page);
            await checkConversationHeader(page);
        }
        await checkDocksAndDiscovery(page, viewport.width);
        await checkA2ADetails(page, viewport.width);
        await checkLearning(page, viewport.width);
        await page.goto(new URL("/discovery", baseUrl).href, { waitUntil: "networkidle" });
        await capture(page, `discovery-${viewport.width}`);
        await page.goto(new URL("/design-system", baseUrl).href, { waitUntil: "networkidle" });
        await page.getByRole("button", { name: "New conversation", exact: true }).click();
        await page.waitForFunction(() => document.querySelector("section > output")?.textContent === "1 activations");
        await page.getByRole("group", { name: "Execution mode" }).getByRole("button", { name: "Manual", exact: true }).click();
        await page.waitForFunction(() => document.querySelector('[aria-label="Execution mode"] button:last-child')?.getAttribute("aria-pressed") === "true");
        await checkConversationMarkdown(page);
        await capture(page, `design-system-${viewport.width}`);
        await page.emulateMedia({ reducedMotion: "reduce" });
        assert.equal(await page.locator(".spinner").evaluate(element => getComputedStyle(element).animationName), "none");
        await page.emulateMedia({ reducedMotion: "no-preference" });
        const missing = await page.goto(new URL("/missing-design-test", baseUrl).href, { waitUntil: "networkidle" });
        assert.equal(missing.status(), 404);
        await page.getByRole("heading", { name: "Page not found", exact: true }).waitFor();
        await capture(page, `not-found-${viewport.width}`);
        await context.close();
        currentPage = null;
        console.log(`Home, Flow, docks, Discovery, Learn and design system ${viewport.width}x${viewport.height}: passed`);
    }
    assert.deepEqual(errors, [], "No unhandled browser errors");
    assert.deepEqual(imageRequests, [], "Untrusted Markdown and raw HTML images must never make requests");
    console.log(`Screenshots: ${screenshots}`);
} catch (error) {
    if (currentPage && !currentPage.isClosed()) await currentPage.screenshot({ path: path.join(screenshots, "failure.png"), fullPage: true });
    console.error(error);
    process.exitCode = 1;
} finally {
    await browser.close();
}

async function checkVersion(page, response) {
    const label = page.locator(".app-header .version");
    await label.waitFor({ state: "visible" });
    assert.equal((await label.textContent()).trim(), `v${applicationVersion}`, "The header renders the release version");
    const metadata = page.locator('meta[name="application-version"]');
    assert.equal(await metadata.count(), 1, "Navigation must not duplicate the version meta tag");
    assert.equal(await metadata.getAttribute("content"), applicationVersion);
    await page.evaluate(() => document.fonts.ready);
    const baselines = await page.evaluate(() =>
        Array.from(document.querySelectorAll(".app-header h1, .app-header .version"), element => {
            const marker = document.createElement("span");
            marker.style.cssText = "display: inline-block; width: 0; height: 0; vertical-align: baseline";
            element.append(marker);
            const baseline = marker.getBoundingClientRect().top;
            marker.remove();
            return baseline;
        }));
    assert.equal(baselines.length, 2);
    assert.ok(Math.abs(baselines[0] - baselines[1]) <= 1, "The version aligns with the product title's text baseline");
    const labelBounds = await label.boundingBox();
    const headerBounds = await page.locator(".app-header").boundingBox();
    assert.ok(labelBounds.x >= headerBounds.x && labelBounds.x + labelBounds.width <= headerBounds.x + headerBounds.width + 1,
        "The version label fits inside the shared header");
    if (response) {
        assert.equal(response.headers()["x-agenticlab-version"], applicationVersion);
        assert.equal(response.headers()["x-ent-lab-version"], undefined);
        const versions = await page.evaluate(html => {
            const document = new DOMParser().parseFromString(html, "text/html");
            return Array.from(document.querySelectorAll('meta[name="application-version"]'), element => element.getAttribute("content"));
        }, await response.text());
        assert.deepEqual(versions, [applicationVersion], "The initial HTML includes the release version without JavaScript");
    }
}

async function checkTeaser(page, viewport) {
    const requests = [];
    page.on("request", request => requests.push(new URL(request.url()).pathname));
    await page.clock.install({ time: new Date("2026-01-01T00:00:00Z") });
    await page.clock.pauseAt(new Date("2026-01-01T00:01:00Z"));
    await page.goto(new URL("/", baseUrl).href, { waitUntil: "networkidle" });
    const teaser = page.locator("agentic-home-teaser[data-ready]");
    await teaser.waitFor();
    await page.evaluate(() => document.fonts.ready);
    const titles = ["Why agents matter", "What is an agent?", "Why Agentic Lab?"];
    assert.deepEqual(await teaser.locator("h3").allTextContents(), titles);
    assert.equal(await teaser.getAttribute("aria-roledescription"), "carousel");
    const panels = teaser.locator(".teaser-panels");
    assert.equal(await panels.getAttribute("aria-live"), "off");
    assert.deepEqual(await teaser.locator("button").evaluateAll(buttons => buttons.map(button => button.dataset.teaserAction)),
        ["toggle", "previous", "next"], "Rotation control comes first in visual and tab order");
    const count = page.locator("[data-teaser-count]");
    const previous = page.getByRole("button", { name: "Previous message", exact: true });
    const next = page.getByRole("button", { name: "Next message", exact: true });
    const toggle = page.locator('[data-teaser-action="toggle"]');
    const layout = () => page.locator(".home-title, .home-purpose, .destinations, .destination, .home-teaser, .teaser-controls")
        .evaluateAll(elements => elements.map(element => element.getBoundingClientRect().toJSON()));
    const initialLayout = await layout();

    async function assertMessage(index, live = "off") {
        assert.equal(await count.textContent(), `${index + 1} / 3`);
        assert.equal(await teaser.locator("[data-active] h3").textContent(), titles[index]);
        assert.equal(await panels.getAttribute("aria-live"), live, "Only stopped rotation announces message changes");
        assert.equal(await count.getAttribute("aria-live"), "off", "The counter does not duplicate message announcements");
        assert.equal(await page.locator(".home-purpose").isVisible(), true, "The purpose remains visible for every message");
        assert.equal(await teaser.getByRole("group").count(), 1, "Only the active message is exposed to accessibility");
        assert.ok(await teaser.locator("[data-teaser-slide]:not([data-active])")
            .evaluateAll(slides => slides.every(slide => slide.inert && slide.getAttribute("aria-hidden") === "true")));
        const icons = await teaser.locator(".teaser-control svg:visible")
            .evaluateAll(elements => elements.map(element => element.getBoundingClientRect().toJSON()));
        assert.equal(icons.length, 3, "All playback controls show their active icon");
        assert.ok(icons.every(bounds => bounds.width >= 16 && bounds.height >= 16), "Playback icons have stable, nonzero dimensions");
        assert.deepEqual(await layout(), initialLayout, "Messages and controls do not shift the page");
    }

    async function advance(milliseconds, index, live = "off") {
        await page.clock.runFor(milliseconds);
        await assertMessage(index, live);
    }

    await assertMessage(0);
    for (const index of [1, 2, 0]) await advance(7000, index);
    await page.getByRole("button", { name: "Pause rotation", exact: true }).click();
    await page.mouse.move(0, 0);
    await advance(14000, 0, "polite");
    await previous.click();
    await assertMessage(2, "polite");
    await next.click();
    await assertMessage(0, "polite");
    await page.mouse.move(0, 0);
    await page.keyboard.press("Shift+Tab");
    assert.ok(await previous.evaluate(element => document.activeElement === element), "Previous precedes Next in tab order");
    await page.keyboard.press("Shift+Tab");
    assert.ok(await toggle.evaluate(element => element.matches(":focus-visible")), "Keyboard playback control has visible focus");
    assert.equal(await toggle.getAttribute("aria-label"), "Play rotation");
    await page.keyboard.press("Enter");
    await advance(7000, 1);
    await page.keyboard.press("Tab");
    assert.ok(await previous.evaluate(element => document.activeElement === element), "Rotation control precedes Previous in tab order");
    await advance(14000, 1, "polite");
    await page.getByRole("link", { name: "Live flow", exact: true }).focus();
    await page.keyboard.press("ArrowRight");
    await advance(7000, 1, "polite");
    await next.focus();
    await page.keyboard.press("ArrowLeft");
    await assertMessage(0, "polite");
    await page.keyboard.press("ArrowRight");
    await assertMessage(1, "polite");

    await page.reload({ waitUntil: "networkidle" });
    await teaser.waitFor();
    await teaser.hover();
    await advance(14000, 0);
    await page.mouse.move(0, 0);
    await advance(7000, 1);
    await page.evaluate(() => {
        Object.defineProperty(document, "hidden", { configurable: true, value: true });
        document.dispatchEvent(new Event("visibilitychange"));
    });
    await advance(14000, 1);
    await page.evaluate(() => {
        delete document.hidden;
        document.dispatchEvent(new Event("visibilitychange"));
    });
    await advance(7000, 2);
    await page.emulateMedia({ reducedMotion: "reduce" });
    await page.getByRole("button", { name: "Play rotation", exact: true }).waitFor();
    await advance(14000, 2, "polite");
    assert.equal(await teaser.locator("[data-active]").evaluate(element => getComputedStyle(element).transitionDuration), "0s");
    await page.reload({ waitUntil: "networkidle" });
    await teaser.waitFor();
    await advance(21000, 0, "polite");
    await next.click();
    await assertMessage(1, "polite");
    await capture(page, `home-teaser-${viewport.width}`);
    await page.getByRole("button", { name: "Play rotation", exact: true }).click();
    await page.mouse.move(0, 0);
    await advance(7000, 2);
    await page.getByRole("button", { name: "Pause rotation", exact: true }).click();
    await page.mouse.move(0, 0);
    await page.emulateMedia({ reducedMotion: "no-preference" });
    await advance(14000, 2, "polite");

    await page.getByRole("button", { name: "Play rotation", exact: true }).click();
    await page.mouse.move(0, 0);
    const detached = await teaser.elementHandle();
    await detached.evaluate(element => element.remove());
    await page.emulateMedia({ reducedMotion: "reduce" });
    await page.clock.runFor(21000);
    assert.equal(await detached.evaluate(element => element.querySelector("[data-teaser-count]").textContent), "3 / 3",
        "Disconnected teaser has no active timer or media listener");
    await page.emulateMedia({ reducedMotion: "no-preference" });
    await detached.evaluate(element => document.querySelector(".home-content").append(element));
    await teaser.waitFor();
    await assertMessage(0);
    await advance(7000, 1);
    await advance(7000, 2);
    await detached.dispose();
    assert.equal(requests.some(route => route.startsWith("/_blazor")), false, "Teaser interactions never start a server circuit");
    assert.equal(await page.evaluate(() => localStorage.length), 0, "Teaser interactions do not save preferences");
}

async function checkHome(page, viewport) {
    const requests = [];
    const recordRequest = request => requests.push(new URL(request.url()).pathname);
    page.on("request", recordRequest);
    const response = await page.goto(new URL("/", baseUrl).href, { waitUntil: "networkidle" });
    page.off("request", recordRequest);
    assert.equal(response.status(), 200);
    await page.locator("agentic-home-teaser[data-ready]").waitFor();
    await page.getByRole("heading", { name: "Agentic Lab", level: 1, exact: true }).waitFor();
    assert.equal(await page.getByRole("heading", { name: "Agentic Lab", exact: true }).count(), 1, "The product heading is not repeated");
    assert.equal(await page.getByRole("heading", { name: "Choose where to start", level: 2, exact: true }).count(), 1);
    assert.match(await page.locator(".home-purpose").textContent(), /Agentic Lab demystifies agentic AI\./);
    assert.equal(await page.getByRole("link", { name: "Agentic Lab home", exact: true }).getAttribute("href"), "/");
    assert.equal(await page.locator(".flow-app, .learn-app").count(), 0, "Home does not mount either destination");
    assert.equal(requests.some(route => route === "/_blazor" || route === "/_blazor/negotiate"), false,
        "Home does not start an interactive server circuit");
    assert.equal(await page.evaluate(() => localStorage.length), 0, "Home does not write preferences");

    const destinations = page.getByRole("navigation", { name: "Agentic Lab destinations", exact: true });
    assert.deepEqual(await destinations.locator("a").evaluateAll(links => links.map(link => link.getAttribute("href"))), ["/flow", "/learn"]);
    const geometry = await destinations.locator("a").evaluateAll(links => links.map(link => {
        const bounds = link.getBoundingClientRect();
        return { x: bounds.x, y: bounds.y, width: bounds.width, height: bounds.height, bottom: bounds.bottom };
    }));
    assert.ok(Math.abs(geometry[0].width - geometry[1].width) < 1, "Destinations have equal width");
    assert.ok(Math.abs(geometry[0].height - geometry[1].height) < 1, "Destinations have equal height");
    assert.ok(geometry.every(bounds => bounds.y >= 0 && bounds.bottom <= viewport.height), "Both choices fit the first viewport");
    if (viewport.width > 900) assert.ok(geometry[1].x > geometry[0].x, "Wide home shows both destinations side by side");
    else assert.ok(geometry[1].y >= geometry[0].bottom, "Narrow home stacks destinations without overlap");
    for (const icon of await page.locator(".destination-icon").all()) {
        const mask = await icon.evaluate(element => getComputedStyle(element).maskImage);
        const asset = new URL(mask.match(/url\(["']?(.*?)["']?\)/)[1], page.url());
        assert.equal(asset.origin, new URL(baseUrl).origin, "Home icons are local assets");
        assert.ok((await page.request.get(asset.href)).ok(), "Home icon is available");
    }
    await capture(page, `home-${viewport.width}`);
    await page.emulateMedia({ reducedMotion: "reduce" });
    assert.equal(await destinations.locator("a").first().evaluate(element => getComputedStyle(element).transitionDuration), "0s");
    await page.emulateMedia({ reducedMotion: "no-preference" });
    if (viewport.width === 1440) {
        await page.evaluate(() => document.documentElement.style.zoom = "2");
        await capture(page, "home-200-percent");
        await page.evaluate(() => document.documentElement.style.zoom = "");
    }

    await followHomeLinkWithKeyboard(page, "Live flow", "/flow");
    assert.equal((await page.reload({ waitUntil: "networkidle" })).status(), 200, "Flow supports direct reloads");
    await page.locator(".flow-app").waitFor();
    assert.equal(await page.getByRole("link", { name: "Agent guide", exact: true }).getAttribute("href"), "/learn");
    await page.getByRole("link", { name: "Agentic Lab home", exact: true }).click();
    await page.locator(".home-app").waitFor();
    await followHomeLinkWithKeyboard(page, "Agent guide", "/learn");
    await page.getByRole("heading", { name: "Intro", exact: true }).waitFor();
    assert.equal(await page.getByRole("link", { name: "Agentic Lab home", exact: true }).getAttribute("href"), "/");
    assert.equal(await page.getByRole("link", { name: "Live flow", exact: true }).getAttribute("href"), "/flow");
    await page.getByRole("link", { name: "The agent loop", exact: true }).click();
    await page.waitForURL(url => url.pathname === "/learn" && url.searchParams.get("stage") === "agent-loop");
    assert.equal((await page.reload({ waitUntil: "networkidle" })).status(), 200);
    await page.getByRole("heading", { name: "The agent loop", exact: true }).waitFor();
    assert.equal(await page.getByRole("link", { name: "Open live flow", exact: true }).getAttribute("href"), "/flow");
    await page.goBack({ waitUntil: "networkidle" });
    await page.getByRole("heading", { name: "Intro", exact: true }).waitFor();
    await page.goForward({ waitUntil: "networkidle" });
    await page.getByRole("heading", { name: "The agent loop", exact: true }).waitFor();
    await page.getByRole("link", { name: "Open live flow", exact: true }).click();
    await page.waitForURL(url => url.pathname === "/flow");
    await page.locator(".flow-app").waitFor();
    await page.getByRole("link", { name: "Agentic Lab home", exact: true }).click();
    await page.locator(".home-app").waitFor();
    await page.locator("agentic-home-teaser[data-ready]").waitFor();
    assert.equal(await page.locator("[data-teaser-count]").textContent(), "1 / 3", "Returning Home initializes one fresh teaser");
}

async function followHomeLinkWithKeyboard(page, name, route) {
    const link = page.getByRole("link", { name, exact: true });
    for (let attempt = 0; attempt < 8; attempt++) {
        await page.keyboard.press("Tab");
        if (await link.evaluate(element => document.activeElement === element)) break;
    }
    assert.ok(await link.evaluate(element => element.matches(":focus-visible") && getComputedStyle(element).outlineStyle === "solid"),
        `${name}: keyboard focus is visible`);
    await page.keyboard.press("Enter");
    await page.waitForURL(url => url.pathname === route);
}

async function checkConversationMarkdown(page) {
    const reply = page.locator(".markdown-example .reply-markdown");
    const source = page.locator("#markdown-source");
    const original = await source.inputValue();
    await reply.getByRole("heading", { name: "Project summary" }).waitFor();
    for (const selector of ["h2", "strong", "em", "ul", "ol", "li", "code", "pre", "blockquote", "table", "th", "td"]) {
        assert.ok(await reply.locator(selector).count(), `Markdown must render ${selector}`);
    }
    assert.equal(await reply.locator("a").count(), 3);
    const links = await reply.locator("a").evaluateAll(anchors => anchors.map(anchor => ({
        protocol: new URL(anchor.href).protocol, target: anchor.target, rel: anchor.rel,
    })));
    assert.deepEqual(links.map(link => link.protocol), ["https:", "http:", "mailto:"]);
    assert.ok(links.every(link => link.target === "_blank" && link.rel === "noopener noreferrer"));
    const text = await reply.innerText();
    assert.ok(text.includes("Image description"));
    assert.ok(text.includes('<img src="https://images.invalid/raw-sentinel.png"'));
    assert.ok(text.includes("<script>window.markdownExecuted=true</script>"));
    await assertSafeReply();
    const styles = await reply.locator("pre code").evaluate(code => ({
        font: getComputedStyle(code).fontFamily,
        size: getComputedStyle(code).fontSize,
        whitespace: getComputedStyle(code.parentElement).whiteSpace,
        overflow: getComputedStyle(code.parentElement).overflowX,
    }));
    assert.ok(styles.font.includes("IBM Plex Mono"), "Generated code must receive scoped typography");
    assert.equal(styles.size, "12px");
    assert.equal(styles.whitespace, "pre");
    assert.equal(styles.overflow, "auto");
    await reply.getByRole("link", { name: "Details", exact: true }).focus();
    await page.keyboard.press("Tab");
    assert.equal(await page.evaluate(() => document.activeElement?.textContent), "HTTP");
    assert.equal(await reply.getByRole("link", { name: "HTTP", exact: true }).evaluate(link => getComputedStyle(link).outlineStyle), "solid");
    await assertBounds();
    await reply.screenshot({ path: path.join(screenshots, `markdown-${page.viewportSize().width}.png`) });

    for (const snapshot of [
        { source: "```html\n<img src='https://images.invalid/partial.png'>", text: "<img src=", selector: "pre code" },
        { source: "[Pending](https://", text: "[Pending]", selector: "p" },
        { source: "- First\n- Sec", text: "Sec", selector: "ul" },
        { source: "| Name | Value |\n| --- | --- |\n| First | Sec", text: "Sec", selector: "table" },
        { source: "[Blocked](javascript:alert%281%29)", text: "Blocked", selector: "p" },
        { source: "", text: "", selector: null },
        { source: "## Updated\n\n**Complete**", text: "Complete", selector: "strong" },
    ]) {
        await source.fill(snapshot.source);
        await page.waitForFunction(({ text, selector }) => {
            const reply = document.querySelector(".markdown-example .reply-markdown");
            return text === "" ? reply.textContent.trim() === "" : reply.textContent.includes(text) && reply.querySelector(selector);
        }, snapshot);
        await assertSafeReply();
        await assertBounds();
    }
    await source.fill(original);
    await reply.getByRole("heading", { name: "Project summary" }).waitFor();
    await page.evaluate(() => document.documentElement.style.zoom = "2");
    await assertBounds();
    await reply.screenshot({ path: path.join(screenshots, `markdown-zoom-${page.viewportSize().width}.png`) });
    await page.evaluate(() => document.documentElement.style.zoom = "");

    async function assertSafeReply() {
        assert.equal(await reply.locator("img, script, iframe, style, object, embed, video, audio, source, [src], [srcset], [onerror], [onmouseover]").count(), 0);
        assert.equal(await page.evaluate(() => window.markdownExecuted), undefined);
        assert.ok(await reply.locator("a").evaluateAll(anchors => anchors.every(anchor => ["http:", "https:", "mailto:"].includes(new URL(anchor.href).protocol))));
    }

    async function assertBounds() {
        const bounds = await reply.evaluate(element => ({
            pageWidth: document.documentElement.clientWidth,
            pageScroll: document.documentElement.scrollWidth,
            width: element.clientWidth,
            scroll: element.scrollWidth,
        }));
        assert.ok(bounds.pageScroll <= bounds.pageWidth + 1, "Markdown must not widen the page");
        assert.ok(bounds.scroll <= bounds.width + 1, "Only code and table payloads may scroll horizontally");
    }
}

async function checkHosts(page, width) {
    const hosts = await page.locator("#host option").evaluateAll(options => options.map(option => ({
        key: option.value, label: option.label,
    })));
    const keys = hosts.map(host => host.key);
    assert.equal(keys[0], "default", "Default is always first");
    assert.equal(await page.locator("#host").inputValue(), "default", "Fresh contexts start with Default");
    if (expectedHosts) assert.deepEqual(keys, expectedHosts, "Only configured hosts are available");

    async function restore(stored, expected) {
        await page.evaluate(key => localStorage.setItem("agenticlab-vendor", key), stored);
        await openFlow(page);
        assert.equal(await page.locator("#host").inputValue(), expected, `Saved host ${stored}`);
        assert.ok(await page.locator("#agent option").count(), "Every available host offers an available agent");
    }

    for (const host of hosts) {
        await restore(host.key.toUpperCase(), host.key);
        const icon = page.locator(".selected-vendor .brand-icon");
        if (host.key === "default") assert.equal(await icon.count(), 0, "Default uses neutral branding");
        if (await icon.count()) {
            const mask = await icon.evaluate(element => getComputedStyle(element).maskImage);
            const matched = mask.match(/url\(["']?([^"')]+)["']?\)/);
            assert.ok(matched, `Host ${host.key} has a CSS mask`);
            const asset = new URL(matched[1], page.url());
            assert.equal(asset.origin, new URL(baseUrl).origin, "Brand assets are local");
            assert.match(asset.pathname, /\/_content\/[^/]+\/host\.svg$/);
            const response = await page.request.get(asset.href);
            assert.ok(response.ok(), `Host ${host.key} logo loads`);
            assert.match(response.headers()["content-type"], /image\/svg\+xml/);
            const painted = await page.evaluate(async url => {
                const image = new Image();
                image.src = url;
                await image.decode();
                const canvas = document.createElement("canvas");
                canvas.width = canvas.height = 24;
                const context = canvas.getContext("2d");
                context.drawImage(image, 0, 0, 24, 24);
                return context.getImageData(0, 0, 24, 24).data.some((value, index) => index % 4 === 3 && value > 0);
            }, asset.href);
            assert.ok(painted, `Host ${host.key} logo is nonblank`);
            const visible = page.locator(".brand-icon:visible").first();
            const bounds = await visible.boundingBox();
            assert.ok(bounds && bounds.width >= 20 && bounds.height >= 20, "Visible host icon has stable dimensions");
            await capture(page, `host-${host.key}-${width}`);
        }
    }
    if (width === 1440) {
        for (const [stored, expected] of Object.entries(legacyHosts)) await restore(stored, expected);
    }
    await restore("unavailable-smoke-host", "default");
    await page.evaluate(() => localStorage.removeItem("agenticlab-vendor"));
}

async function checkExecutionControls(page) {
    const controls = page.getByRole("group", { name: "Execution controls", exact: true });
    const buttons = controls.getByRole("button");
    assert.deepEqual(await buttons.evaluateAll(elements => elements.map(element => element.getAttribute("aria-label"))), ["Pause", "Next", "Stop"]);
    assert.ok(await buttons.evaluateAll(elements => elements.every(element => element.disabled && element.classList.contains("icon-only"))));
    const initial = await buttons.evaluateAll(elements => elements.map(element => {
        const bounds = element.getBoundingClientRect();
        const toolbar = element.closest(".run-controls").getBoundingClientRect();
        return { x: bounds.x - toolbar.x, y: bounds.y - toolbar.y, width: bounds.width, height: bounds.height };
    }));
    const modes = page.getByRole("group", { name: "Execution mode", exact: true });
    await modes.getByRole("button", { name: "Manual", exact: true }).click();
    await page.waitForFunction(() => document.querySelector('[aria-label="Execution mode"] button:last-child')?.getAttribute("aria-pressed") === "true");
    const manual = await buttons.evaluateAll(elements => elements.map(element => {
        const bounds = element.getBoundingClientRect();
        const toolbar = element.closest(".run-controls").getBoundingClientRect();
        return { x: bounds.x - toolbar.x, y: bounds.y - toolbar.y, width: bounds.width, height: bounds.height };
    }));
    assert.deepEqual(manual, initial, "Execution action slots must not move when mode changes");
    assert.ok(await buttons.evaluateAll(elements => elements.every(element => element.disabled)));
    await modes.getByRole("button", { name: "Auto", exact: true }).click();
    await page.waitForFunction(() => document.querySelector('[aria-label="Execution mode"] button:first-child')?.getAttribute("aria-pressed") === "true");
}

async function checkConversationHeader(page) {
    const header = page.locator(".conversation-head");
    const newConversation = header.getByRole("button", { name: "New conversation", exact: true });
    assert.equal(await newConversation.count(), 1);
    assert.equal(await newConversation.getAttribute("title"), "New conversation");
    assert.ok(await newConversation.evaluate(button => button.classList.contains("icon-only")));
    assert.equal(await page.locator(".chat-composer").getByRole("button", { name: "New conversation", exact: true }).count(), 0);
    const fits = await header.evaluate(element => {
        const boundary = element.getBoundingClientRect();
        const buttons = [...element.querySelectorAll("button")];
        const bounds = buttons.map(button => button.getBoundingClientRect());
        return buttons.every((button, index) => button.scrollWidth <= button.clientWidth + 1
            && bounds[index].left >= boundary.left && bounds[index].right <= boundary.right
            && (index === 0 || bounds[index].left >= bounds[index - 1].right - 1));
    });
    assert.ok(fits, "Conversation tabs and header actions fit without overlapping");
}

async function checkDocksAndDiscovery(page, width) {
    await openFlow(page);
    const hostSubtitle = page.locator(".node.harness > .subtitle").first();
    assert.equal(await hostSubtitle.innerText(), "Agent service");
    await page.getByRole("button", { name: "View options", exact: true }).click();
    await page.getByRole("checkbox", { name: "Technical labels", exact: true }).check();
    await page.keyboard.press("Escape");
    await page.waitForFunction(() => document.querySelector(".node.harness > .subtitle")?.textContent.includes("AiService"));
    assert.doesNotMatch(await hostSubtitle.innerText(), /Client/, "The compact host describes the service, not the client");
    await page.locator("#message").fill("Preserved while inspecting");
    await page.getByRole("button", { name: "Expand the Execution panel", exact: true }).click();
    await page.locator(".main-panel-body.bottom-max").waitFor();
    await page.getByRole("button", { name: "Restore the Execution panel", exact: true }).click();
    await page.locator(".main-panel-body.bottom-max").waitFor({ state: "hidden" });
    await page.getByRole("button", { name: "Collapse the Execution panel", exact: true }).click();
    await page.locator(".side-bottom.collapsed").waitFor();
    await page.getByRole("button", { name: "Expand the Execution panel", exact: true }).click();
    await page.locator(".side-bottom.collapsed").waitFor({ state: "hidden" });
    await page.getByRole("checkbox", { name: "Learn", exact: true }).check();
    await page.getByRole("button", { name: "View options", exact: true }).click();
    await page.getByRole("checkbox", { name: "Expand harness", exact: true }).check();
    await page.keyboard.press("Escape");
    const anatomy = page.locator(".node.harness.anatomy");
    await anatomy.waitFor();
    const settingsLayer = anatomy.locator(".layer.agent").filter({ has: page.getByRole("button", { name: "Settings", exact: true }) });
    assert.match(await settingsLayer.locator(".layer-body").innerText(), /^Declared model: .+stepping:/,
        "Host settings show the declared model instead of a fixed provider");
    assert.equal(await anatomy.getByRole("button", { name: "Client", exact: true }).count(), 0, "Client is not a host inspector section");
    assert.equal(await anatomy.getByTitle("Learn about the client", { exact: true }).count(), 0, "Client has no anatomy info button");
    await page.getByRole("button", { name: "System prompt", exact: true }).click();
    await page.locator(".details-dock").waitFor({ state: "visible" });
    const learn = page.locator(".learn-dock");
    await learn.waitFor({ state: "visible" });
    await learn.getByRole("button", { name: /^Client\b/ }).click();
    await learn.getByRole("heading", { name: "Client", exact: true }).waitFor();
    assert.match(await learn.locator(".concept-body").innerText(), /outside the harness/);
    assert.equal(await page.locator("#host-detail-heading").innerText(), "System prompt", "Opening Client in Learn preserves Details");
    assert.equal(await page.locator("#message").inputValue(), "Preserved while inspecting");
    const regions = await page.evaluate(() => [".primary-workspace", ".details-dock", ".learn-dock"].map(selector => {
        const bounds = document.querySelector(selector).getBoundingClientRect();
        return { left: bounds.left, right: bounds.right, top: bounds.top, bottom: bounds.bottom, width: bounds.width };
    }));
    if (width >= 1200) {
        assert.ok(regions[0].right <= regions[1].left + 1 && regions[1].right <= regions[2].left + 1, "Docks must not overlap");
        assert.ok(regions.every(region => region.width >= 240), "All expanded regions stay readable");
    } else {
        assert.ok(regions[0].bottom <= regions[1].top + 1 && regions[1].bottom <= regions[2].top + 1, "Narrow docks stack independently");
    }
    await capture(page, `flow-docks-${width}`);
    await page.getByRole("button", { name: "Close details", exact: true }).click();
    await page.locator(".details-dock").waitFor({ state: "hidden" });
    assert.ok(await page.locator(".learn-dock").isVisible(), "Closing Details must not close Learn");
    const trigger = page.getByRole("button", { name: "Discovery", exact: true });
    const dialog = page.getByRole("dialog", { name: "Discovery", exact: true });
    await trigger.click();
    await dialog.waitFor();
    await page.keyboard.press("Tab");
    assert.ok(await dialog.evaluate(element => element.contains(document.activeElement)), "Dialog contains keyboard focus");
    assert.notEqual(await dialog.evaluate(element => getComputedStyle(element, "::backdrop").backgroundColor), "rgba(0, 0, 0, 0)");
    await capture(page, `discovery-overlay-${width}`);
    await page.keyboard.press("Escape");
    await dialog.waitFor({ state: "hidden" });
    assert.equal(await page.evaluate(() => document.activeElement?.textContent.trim()), "Discovery");
    assert.equal(await page.locator("#message").inputValue(), "Preserved while inspecting");
    await trigger.click();
    await dialog.waitFor();
    await page.mouse.click(2, 2);
    await dialog.waitFor({ state: "hidden" });
    assert.equal(await page.locator("#message").inputValue(), "Preserved while inspecting");
}

async function checkA2ADetails(page, width) {
    await openFlow(page);
    await page.getByRole("combobox", { name: "App", exact: true }).selectOption({ label: "Default" });
    await page.waitForFunction(() => [...document.querySelector("#agent").options].some(option => option.value === "Orchestrator"));
    await page.locator("#agent").selectOption("Orchestrator");
    await page.getByRole("button", { name: "Technical", exact: true }).click();
    await page.getByRole("button", { name: "View options", exact: true }).click();
    await page.getByRole("checkbox", { name: "Expand harness", exact: true }).uncheck();
    await page.getByRole("checkbox", { name: "A2A agents", exact: true }).check();
    await page.keyboard.press("Escape");
    const chips = page.locator(".a2a.node-inner .skill-chip .host-section");
    await page.locator(".a2a.node-inner .skill-chip").first().waitFor();
    await page.locator(".a2a-flow .a2a-agent-title h4").first().waitFor();
    assert.equal(await page.locator(".host-section").count(), 0, "Collapsed host shows plain labels, including A2A agents");
    assert.equal(await page.locator(".inspect-icon").count(), 0, "Collapsed host hides all inspect icons");
    await page.getByRole("button", { name: "View options", exact: true }).click();
    await page.getByRole("checkbox", { name: "Expand harness", exact: true }).check();
    await page.keyboard.press("Escape");
    await chips.first().waitFor();
    const names = (await chips.allTextContents()).map(name => name.trim());
    const firstAgent = names[0];
    const learnVisible = await page.getByRole("checkbox", { name: "Learn", exact: true }).isChecked();
    await page.locator("#message").fill("Preserved while inspecting A2A");
    await chips.first().focus();
    await page.keyboard.press("Enter");
    await page.waitForFunction(name => document.querySelector("#host-detail-heading")?.textContent === name
        && document.activeElement?.id === "host-detail-heading", firstAgent);
    assert.equal(await chips.first().getAttribute("aria-expanded"), "true");
    assert.match(await page.locator("#details-content").innerText(), /Agent2Agent \(A2A\)/);
    assert.match(await page.locator("#details-content").innerText(), /No request captured at this position/);
    assert.match(await page.locator("#details-content").innerText(), /model settings and tools are not exposed/);
    await capture(page, `a2a-details-${width}`);

    await page.getByRole("button", { name: "View options", exact: true }).click();
    await page.getByRole("checkbox", { name: "Expand harness", exact: true }).uncheck();
    await page.keyboard.press("Escape");
    await page.waitForFunction(() => document.querySelectorAll(".host-section").length === 0);
    assert.equal(await page.locator(".inspect-icon").count(), 0);
    assert.ok(await page.locator(".details-dock").isVisible(), "Collapsing the host keeps A2A Details open");
    assert.equal(await page.locator("#host-detail-heading").textContent(), firstAgent);
    await page.getByRole("button", { name: "View options", exact: true }).click();
    await page.getByRole("checkbox", { name: "Expand harness", exact: true }).check();
    await page.keyboard.press("Escape");
    await chips.first().waitFor();
    assert.equal(await chips.first().getAttribute("aria-expanded"), "true");

    await page.getByRole("button", { name: "Collapse the Details panel", exact: true }).click();
    await page.locator(".details-dock .collapsed").waitFor();
    assert.equal(await chips.first().getAttribute("aria-expanded"), "false");
    await chips.first().click();
    await page.locator("#host-detail-heading").waitFor();
    assert.equal(await chips.first().getAttribute("aria-expanded"), "true");
    if (names.length > 1) {
        await chips.nth(1).click();
        await page.waitForFunction(name => document.querySelector("#host-detail-heading")?.textContent === name, names[1]);
        assert.equal(await chips.first().getAttribute("aria-expanded"), "false");
        assert.equal(await chips.nth(1).getAttribute("aria-expanded"), "true");
    }

    const remoteHeading = page.locator(".a2a-flow .a2a-agent-title").getByRole("button", { name: firstAgent, exact: true });
    await remoteHeading.click();
    await page.waitForFunction(name => document.querySelector("#host-detail-heading")?.textContent === name
        && document.activeElement?.id === "host-detail-heading", firstAgent);
    const ids = await page.locator('[id^="host-section-a2a-"]').evaluateAll(elements => elements.map(element => element.id));
    assert.equal(new Set(ids).size, ids.length, "Remote-agent inspector triggers have unique IDs");
    await page.keyboard.press("Escape");
    await page.locator(".details-dock").waitFor({ state: "hidden" });
    assert.ok(await remoteHeading.evaluate(element => document.activeElement === element), "Closing A2A Details restores the remote heading's focus");

    await page.getByRole("button", { name: "View options", exact: true }).click();
    await page.getByRole("checkbox", { name: "Expand harness", exact: true }).check();
    await page.keyboard.press("Escape");
    const catalogueHeading = page.locator(".a2a.node-inner").getByRole("button", { name: "A2A agents", exact: true });
    await catalogueHeading.click();
    await page.locator("#details-content").getByRole("button", { name: firstAgent, exact: true }).click();
    await page.waitForFunction(name => document.querySelector("#host-detail-heading")?.textContent === name
        && document.activeElement?.id === "host-detail-heading", firstAgent);
    await page.keyboard.press("Escape");
    await page.locator(".details-dock").waitFor({ state: "hidden" });
    assert.ok(await catalogueHeading.evaluate(element => document.activeElement === element), "Drilling into an A2A catalogue entry retains the external focus origin");
    assert.equal(await page.locator("#message").inputValue(), "Preserved while inspecting A2A");
    assert.equal(await page.locator("#agent").inputValue(), selectedAgent);
    assert.equal(await page.getByRole("checkbox", { name: "Learn", exact: true }).isChecked(), learnVisible);
}

async function checkLearning(page, width) {
    await page.goto(new URL("/learn", baseUrl).href, { waitUntil: "networkidle" });
    assert.equal(await page.getByRole("button", { name: "Discovery", exact: true }).count(), 0);
    const introductionCount = await page.locator(".intro-step").count();
    for (let index = 0; index < introductionCount; index++) {
        if (index > 0) await page.getByRole("button", { name: "Next reveal", exact: true }).click();
        await page.waitForFunction(count => document.querySelector(".intro-count")?.textContent.trim() === count,
            `${index + 1} / ${introductionCount}`);
        assert.equal(await page.locator(".intro-step.current").count(), 1);
        assert.equal(await page.locator('.intro-diagram[data-display="static"]').count(), 1);
        await capture(page, index === 0 ? `learn-${width}` : `learn-intro-${index + 1}-${width}`);
    }
    assert.equal(await page.locator(".intro-application .intro-harness .actor-search").count(), 1);
    assert.ok(await page.getByRole("button", { name: "Next reveal", exact: true }).isDisabled());
    assert.ok(await page.getByRole("button", { name: "Show complete diagram", exact: true }).isDisabled());
    if (width >= 900) await checkLearnKeyboard(page, width);
    await checkFoundationReveals(page, width);
    await checkTurnOutcomes(page, width);
    const stages = ["model-to-agent", "anatomy-of-agent", "where-to-run", "agent-loop", "wider-ecosystem"];
    for (const stage of stages) {
        await page.goto(new URL(`/learn?stage=${stage}`, baseUrl).href, { waitUntil: "networkidle" });
        const complete = page.getByRole("button", { name: "Show complete diagram", exact: true });
        if (await complete.count()) {
            await complete.click();
            await page.waitForFunction(() => Number(document.querySelector("[data-beat]")?.dataset.beat) > 0);
        }
        await capture(page, `learn-${stage}-${width}`);
    }
}

async function checkLearnKeyboard(page, width) {
    const lessons = await page.locator(".stage-list a").evaluateAll(links => links.map(link => ({
        stage: new URL(link.href).searchParams.get("stage")
    })));
    assert.ok(lessons.length > 0, "The guide advertises navigable lessons");
    const position = async (stage, beat) => page.waitForFunction(({ stage, beat }) => {
        const root = document.querySelector(".learn-app");
        const current = new URL(location.href).searchParams.get("stage");
        const introduction = document.querySelector(".intro-count");
        const actualBeat = introduction ? Number(introduction.textContent.split("/")[0]) - 1 :
            Number(document.querySelector("[data-beat]")?.dataset.beat ?? 0);
        return root?.dataset.keyboardReady === "true" && !root.dataset.keyboardBusy && current === stage && actualBeat === beat;
    }, { stage, beat });
    const stepCount = async () => page.evaluate(() => {
        const counter = document.querySelector(".intro-count, .beat-count, .step-count");
        return counter ? Number(counter.textContent.split("/")[1]) : 1;
    });
    const load = async stage => {
        await page.goto(new URL(`/learn?stage=${stage}`, baseUrl).href, { waitUntil: "networkidle" });
        await position(stage, 0);
    };
    const move = async (key, stage, beat) => {
        await page.keyboard.press(key);
        await position(stage, beat);
    };

    const first = lessons[0];
    const last = lessons.at(-1);
    await load(first.stage);
    assert.equal(await page.evaluate(() => document.activeElement === document.body), true);
    await move("ArrowLeft", first.stage, 0);
    for (const [index, lesson] of lessons.entries()) {
        lesson.count = await stepCount();
        lesson.hasSelector = await page.locator(".foundation-lesson select").count() > 0;
        lesson.hasOutcomes = await page.locator("#turn-outcome").count() > 0;
        for (let beat = 1; beat < lesson.count; beat++) await move("ArrowRight", lesson.stage, beat);
        if (index < lessons.length - 1) {
            await move("ArrowRight", lessons[index + 1].stage, 0);
            await page.waitForFunction(() => document.activeElement?.id === "stage-title");
        }
    }
    await move("ArrowRight", last.stage, last.count - 1);
    await capture(page, `learn-keyboard-end-${width}`);
    for (let index = lessons.length - 1; index >= 0; index--) {
        const lesson = lessons[index];
        for (let beat = lesson.count - 2; beat >= 0; beat--) await move("ArrowLeft", lesson.stage, beat);
        if (index > 0) {
            const previous = lessons[index - 1];
            await move("ArrowLeft", previous.stage, previous.count - 1);
            await page.waitForFunction(() => document.activeElement?.id === "stage-title");
            if (previous.hasOutcomes) assert.equal(await page.locator(".step-evidence").count(), 1);
        }
    }
    await move("ArrowLeft", first.stage, 0);

    const progressive = lessons.find(lesson => lesson.count > 1);
    if (progressive) {
        await load(progressive.stage);
        await page.getByRole("button", { name: /^Next (step|reveal)$/ }).focus();
        const urlBeforeReveal = page.url();
        for (let beat = 1; beat < progressive.count; beat++) await move("ArrowRight", progressive.stage, beat);
        assert.equal(page.url(), urlBeforeReveal, "Reveals do not change the stage URL");
        await page.waitForFunction(() => document.activeElement?.id === "stage-title");
        await page.reload({ waitUntil: "networkidle" });
        await position(progressive.stage, 0);
        const next = lessons[lessons.indexOf(progressive) + 1];
        if (next) {
            for (let beat = 1; beat < progressive.count; beat++) await move("ArrowRight", progressive.stage, beat);
            await move("ArrowRight", next.stage, 0);
            await page.goBack();
            await position(progressive.stage, 0);
            await page.goForward();
            await position(next.stage, 0);
            await move("ArrowLeft", progressive.stage, progressive.count - 1);
        }
    }

    const selectorLesson = lessons.find(lesson => lesson.hasSelector);
    if (selectorLesson) {
        await load(selectorLesson.stage);
        for (let beat = 1; beat < selectorLesson.count; beat++) await move("ArrowRight", selectorLesson.stage, beat);
        const selector = page.locator(".foundation-lesson select").first();
        await selector.focus();
        assert.equal(await selector.evaluate(element => element === document.activeElement), true);
        assert.equal(await selector.evaluate(element => element.dispatchEvent(
            new KeyboardEvent("keydown", { key: "ArrowRight", bubbles: true, cancelable: true }))), true);
        await page.keyboard.press("ArrowLeft");
        await position(selectorLesson.stage, selectorLesson.count - 1);
    }
    const beforeIgnoredKeys = await page.evaluate(() => ({
        stage: new URL(location.href).searchParams.get("stage"),
        beat: document.querySelector(".intro-count") ?
            Number(document.querySelector(".intro-count").textContent.split("/")[0]) - 1 :
            Number(document.querySelector("[data-beat]")?.dataset.beat ?? 0)
    }));
    await page.locator("#stage-title").focus();
    for (const options of [{ altKey: true }, { ctrlKey: true }, { shiftKey: true }, { metaKey: true }, { repeat: true }, { isComposing: true }]) {
        const allowed = await page.locator("#stage-title").evaluate((element, options) => element.dispatchEvent(
            new KeyboardEvent("keydown", { key: "ArrowRight", bubbles: true, cancelable: true, ...options })), options);
        assert.equal(allowed, true, "Modifiers, composition and held-key repeats remain untouched");
    }
    await position(beforeIgnoredKeys.stage, beforeIgnoredKeys.beat);

    const outcomeLesson = lessons.find(lesson => lesson.hasOutcomes);
    const following = lessons[lessons.indexOf(outcomeLesson) + 1];
    if (outcomeLesson && following) {
        await load(outcomeLesson.stage);
        const scenarios = await page.locator("#turn-outcome option").evaluateAll(options => options.map(option => option.value));
        for (const scenario of scenarios) {
            await load(outcomeLesson.stage);
            await page.getByText("Other outcomes", { exact: true }).click();
            await page.locator("#turn-outcome").selectOption(scenario);
            await page.locator(`.turn-lesson[data-scenario="${scenario}"][data-beat="0"]`).waitFor();
            await page.locator("#stage-title").focus();
            const count = await stepCount();
            for (let beat = 1; beat < count; beat++) await move("ArrowRight", outcomeLesson.stage, beat);
            assert.equal(await page.locator(".step-evidence").count(), 1);
            await move("ArrowRight", following.stage, 0);
            await move("ArrowLeft", outcomeLesson.stage, outcomeLesson.count - 1);
            assert.equal(await page.locator(".step-evidence").count(), 1);
        }
    }

    await load(first.stage);
    await page.getByRole("link", { name: "Live flow", exact: true }).click();
    await page.locator(".learn-app").waitFor({ state: "detached" });
    const nativeArrow = await page.evaluate(() => document.body.dispatchEvent(
        new KeyboardEvent("keydown", { key: "ArrowRight", bubbles: true, cancelable: true })));
    assert.equal(nativeArrow, true, "Leaving Learn removes its arrow listener");
    await page.goBack();
    await position(first.stage, 0);
    if (first.count > 1) await move("ArrowRight", first.stage, 1);
    await capture(page, `learn-keyboard-return-${width}`);
}

async function checkFoundationReveals(page, width) {
    await page.goto(new URL("/learn?stage=model-to-agent", baseUrl).href, { waitUntil: "networkidle" });
    const overview = page.locator('.foundation-lesson[data-story="model-to-agent"][data-beat="0"]');
    assert.deepEqual(await overview.locator("[data-node]:visible").evaluateAll(elements => elements.map(element => element.dataset.node)), ["harness", "model"]);
    assert.equal(await overview.locator(".story-toolbar, [data-connection], .application-tools, .exchange-paths").count(), 0);
    assert.match(await overview.innerText(), /Agent = Harness \+ Model/);
    assert.match(await overview.locator('[data-node="harness"]').innerText(), /Handles input, manages context and controls execution/);
    assert.match(await overview.locator('[data-node="model"]').innerText(), /Generates responses from the supplied input/);
    await capture(page, `learn-model-to-agent-complete-${width}`);

    const harnessNodes = ["instructions", "harness-context", "available-tools", "harness-memory", "execution-controls"];
    for (const [stage, lastBeat] of [["inside-the-harness", 5], ["anatomy-of-agent", 9]]) {
        await page.goto(new URL(`/learn?stage=${stage}`, baseUrl).href, { waitUntil: "networkidle" });
        const lesson = page.locator(".foundation-lesson");
        const next = lesson.getByRole("button", { name: "Next reveal", exact: true });
        let previousNodes = [];
        for (let beat = 0; beat <= lastBeat; beat++) {
            if (beat > 0) {
                await next.focus();
                await page.keyboard.press("Enter");
            }
            await page.locator(`.foundation-lesson[data-beat="${beat}"]`).waitFor();
            const nodes = await lesson.locator("[data-node]:visible").evaluateAll(elements => elements.map(element => element.dataset.node));
            for (const id of previousNodes) assert.ok(nodes.includes(id), `${stage}: previous nodes remain visible`);
            const loadedSkillBeat = stage === "anatomy-of-agent" && beat === 9;
            assert.equal(nodes.length, loadedSkillBeat ? beat : beat + 1, `${stage}: one new node per reveal`);
            if (stage === "inside-the-harness") {
                assert.deepEqual(nodes, ["context-management", ...harnessNodes.slice(0, Math.max(0, beat - 1)), ...(beat >= 1 ? ["execution-controls"] : [])]);
                assert.equal(await lesson.locator('.harness-context-aspects [data-node]').count(), Math.max(0, beat - 1));
                assert.equal(await lesson.locator('button[data-node="context-management"]').count(), 0, "Context management is a static parent");
                assert.equal(await lesson.locator('.harness-node[aria-expanded="true"], .harness-example:visible').count(), 0);
                if (beat <= 1) await capture(page, `learn-harness-reveal-${beat}-${width}`);
            }
            if (stage === "anatomy-of-agent") {
                assert.equal(await lesson.locator('[data-node="anatomy-settings"]').isVisible(), beat >= 4);
                assert.equal(await lesson.locator('[data-node="execution-controls"]').isVisible(), beat >= 5);
                assert.equal(await lesson.locator('[data-node="anatomy-skills"]').isVisible(), beat >= 8);
                assert.equal(await lesson.locator(".anatomy-loaded").isVisible(), beat >= 9);
            }
            previousNodes = nodes;
        }
        assert.ok(await next.isDisabled());
        assert.equal(await lesson.locator("details[open]").count(), 0, "Optional details start collapsed");
        await capture(page, `learn-${stage}-complete-${width}`);
        if (stage === "inside-the-harness") await checkHarnessExamples(page, lesson, harnessNodes, width);
        await lesson.getByRole("button", { name: "Previous reveal", exact: true }).click();
        await page.locator(`.foundation-lesson[data-beat="${lastBeat - 1}"]`).waitFor();
        await lesson.getByRole("button", { name: "Show complete diagram", exact: true }).click();
        await page.locator(`.foundation-lesson[data-beat="${lastBeat}"]`).waitFor();
        if (stage === "inside-the-harness") {
            assert.equal(await lesson.locator('[data-node="harness-memory"]').getAttribute("aria-expanded"), "false");
            assert.equal(await lesson.locator('[data-node="execution-controls"]').getAttribute("aria-expanded"), "true");
            assert.equal(await lesson.locator('[data-node="harness-context"]').getAttribute("aria-expanded"), "true");
        }
        if (stage === "anatomy-of-agent") {
            assert.match(await lesson.locator('[data-node="anatomy-task"]').innerText(), /Compare heat pumps and electric heaters/);
            await lesson.getByRole("button", { name: "Coding", exact: true }).click();
            await page.locator('.anatomy-story[data-purpose="coding"]').waitFor();
            await page.locator('.foundation-lesson[data-beat="9"]').waitFor();
            assert.ok(await lesson.locator(".anatomy-loaded").isVisible());
            await lesson.getByRole("button", { name: "Chat", exact: true }).click();
            await page.locator('.anatomy-story[data-purpose="chat"]').waitFor();
            await page.locator('.foundation-lesson[data-beat="9"]').waitFor();
        }
        await lesson.getByRole("button", { name: "Restart", exact: true }).click();
        await page.locator('.foundation-lesson[data-beat="0"]').waitFor();
        assert.equal(await lesson.locator("[data-node]:visible").count(), 1);
        if (stage === "inside-the-harness") {
            assert.equal(await lesson.locator('[data-node="context-management"]').count(), 1);
            assert.equal(await lesson.locator('.harness-node[aria-expanded="true"], .harness-example:visible').count(), 0);
        }
        await capture(page, `learn-${stage}-start-${width}`);
    }
}

async function checkHarnessExamples(page, lesson, nodeIds, width) {
    for (const [index, nodeId] of nodeIds.entries()) {
        const node = lesson.locator(`[data-node="${nodeId}"]`);
        const exampleId = await node.getAttribute("aria-controls");
        const example = lesson.locator(`#${exampleId}`);
        assert.equal(await example.count(), 1, "Each node controls its own example");
        if (index === 0) await node.click();
        else {
            await node.focus();
            await page.keyboard.press(index % 2 ? "Enter" : "Space");
        }
        await example.waitFor({ state: "visible" });
        assert.equal(await node.getAttribute("aria-expanded"), "true");
        assert.equal(await lesson.getAttribute("data-beat"), "5", "Example toggles do not change reveal progress");
        const nodeBox = await node.boundingBox();
        const exampleBox = await example.boundingBox();
        if (width >= 900) {
            assert.ok(exampleBox.x >= nodeBox.x + nodeBox.width - 1, "Example is to the right of its node");
            assert.ok(Math.abs(exampleBox.y - nodeBox.y) < 2, "Example aligns with its own row");
        }
    }
    assert.equal(await lesson.locator(".harness-example:visible").count(), 5, "Examples stay open independently");
    await capture(page, `learn-harness-examples-${width}`);
    const first = lesson.locator(`[data-node="${nodeIds[0]}"]`);
    await first.focus();
    await page.keyboard.press("Enter");
    await lesson.locator(`#${await first.getAttribute("aria-controls")}`).waitFor({ state: "hidden" });
    assert.equal(await lesson.locator(".harness-example:visible").count(), 4, "Closing one example leaves the others open");
}

async function checkTurnOutcomes(page, width) {
    await page.goto(new URL("/learn?stage=agent-loop", baseUrl).href, { waitUntil: "networkidle" });
    const lesson = page.locator(".turn-lesson");
    assert.deepEqual(await lesson.locator("[data-loop-node]").evaluateAll(elements => elements.map(element => element.dataset.loopNode)), ["context", "model", "execute", "answer"]);
    await lesson.getByText("Other outcomes", { exact: true }).click();
    for (const [scenario, lastBeat] of [["read", 7], ["denied", 7], ["missing", 7], ["cancelled", 3], ["limit", 3], ["provided", 3]]) {
        await lesson.locator("select").selectOption(scenario);
        await page.locator(`.turn-lesson[data-scenario="${scenario}"][data-beat="0"]`).waitFor();
        if (scenario === "read") {
            for (let beat = 1; beat <= 5; beat++) {
                await lesson.getByRole("button", { name: "Next step", exact: true }).click();
                await page.locator(`.turn-lesson[data-beat="${beat}"]`).waitFor();
            }
            assert.equal(await lesson.locator('[data-loop-node="context"][aria-current="step"]').count(), 1);
            assert.equal(await lesson.locator(".return-edge.traversed").count(), 1);
            assert.equal(await lesson.locator(".request-summary dd").last().innerText(), "Yes");
        }
        await lesson.getByRole("button", { name: "Show all steps", exact: true }).click();
        await page.locator(`.turn-lesson[data-beat="${lastBeat}"]`).waitFor();
        assert.equal(await lesson.locator('[data-loop-node="answer"]').getAttribute("class").then(value => value.includes("reached")), true);
        assert.equal(await lesson.locator('[data-loop-node="execute"]').getAttribute("class").then(value => value.includes("reached")), true);
        const overflowingNodes = await lesson.locator("[data-loop-node]").evaluateAll(elements => elements
            .filter(element => element.scrollHeight > element.clientHeight + 1 || element.scrollWidth > element.clientWidth + 1)
            .map(element => element.dataset.loopNode));
        assert.deepEqual(overflowingNodes, [], `${scenario}: loop node text fits`);
        if (scenario === "denied" || scenario === "missing") {
            assert.doesNotMatch(await lesson.locator(".text-evidence").allTextContents().then(values => values.join("\n")), /Priya will send/);
        }
        await capture(page, `learn-loop-${scenario}-${width}`);
        await lesson.getByRole("button", { name: "Restart", exact: true }).click();
        await page.locator(`.turn-lesson[data-scenario="${scenario}"][data-beat="0"]`).waitFor();
        assert.equal(await lesson.locator(".loop-node.reached").count(), 1);
    }
}

async function openFlow(page) {
    await page.goto(new URL("/flow", baseUrl).href, { waitUntil: "networkidle" });
    await page.waitForFunction(() => document.querySelector(".chat-log")?._tsStickInit === true);
    await page.locator("#agent option").first().waitFor({ state: "attached" });
}

async function capture(page, name) {
    await page.evaluate(() => document.fonts.ready);
    const metrics = await page.evaluate(() => ({
        overflow: document.documentElement.scrollWidth > innerWidth + 1,
        fonts: [...document.fonts].filter(font => font.status === "loaded").map(font => font.family),
        font: getComputedStyle(document.querySelector("h1")).fontFamily,
        overflowingBars: [...document.querySelectorAll(".app-header, .home-content, .destinations, .destination, .home-teaser, .teaser-panels, .teaser-panel, .teaser-controls, .selection-bar, .conversation-head, .main-panel-head, .run-controls, .discovery-toolbar, .stage-heading")]
            .filter(element => element.getClientRects().length && element.scrollWidth > element.clientWidth + 1)
            .map(element => element.className)
    }));
    assert.equal(metrics.overflow, false, `${name}: no page-level horizontal overflow`);
    assert.deepEqual(metrics.overflowingBars, [], `${name}: all toolbar content fits`);
    assert.match(metrics.font, /IBM Plex Sans/);
    assert.ok(metrics.fonts.some(font => font.includes("IBM Plex Sans")), `${name}: local UI font is loaded`);
    await page.screenshot({ path: path.join(screenshots, `${name}.png`), fullPage: true });
}