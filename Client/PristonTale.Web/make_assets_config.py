"""Writes the game data folder's web.config: IIS only serves file types it knows, so every type in the folder is listed.

Usage: python make_assets_config.py <site assets dir>
"""
import os
import sys


def main():
    root = sys.argv[1]
    extensions = set()
    for _, _, files in os.walk(root):
        for name in files:
            extension = os.path.splitext(name)[1].lower()
            if extension and extension != '.config':
                extensions.add(extension)
    maps = '\n'.join('\t\t\t<remove fileExtension="%s" />\n\t\t\t<mimeMap fileExtension="%s" mimeType="application/octet-stream" />' % (e, e) for e in sorted(extensions))
    text = """<?xml version="1.0" encoding="UTF-8"?>
<!-- Game data: every data file type is listed; some file names contain '+', and request filtering must not hide any folder. -->
<configuration>
	<system.webServer>
		<staticContent>
%s
			<clientCache cacheControlMode="UseMaxAge" cacheControlMaxAge="7.00:00:00" />
		</staticContent>
		<security>
			<requestFiltering allowDoubleEscaping="true">
				<fileExtensions allowUnlisted="true">
					<clear />
				</fileExtensions>
				<hiddenSegments>
					<clear />
				</hiddenSegments>
			</requestFiltering>
		</security>
	</system.webServer>
</configuration>
""" % maps
    with open(os.path.join(root, 'web.config'), 'w', encoding='utf-8', newline='\r\n') as out:
        out.write(text)
    print('%d file types -> %s' % (len(extensions), os.path.join(root, 'web.config')))


if __name__ == '__main__':
    main()
