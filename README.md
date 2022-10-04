# Tool for creating a repro from illink invocation in msbuild

The tool takes an msbuild binlog and looks for illink invocations. It picks one (based on options) and create a repro directory to be abel to run the same illink separately. This means:

* Rewrite the command line for the tool to a new .rsp file
* Copy all input files into the `input` directory
* Redirect output to `out` directory
* Rewrite command line to point to the `input` directory using relative paths only

Ideally the repro directory can be zipped and moved to a different machine and be able to repro the illink invocation exactly.

Typical use:
```
illinkrepro create msbuild.binlog
```

This will find the only illink invocation which failed and create a repro for it in the `repro` subdirectory of the current directory.
Common command line options:
* `-o <path>` - specifies the output directory for the repro. Can be a relative path to the current directory.
* `-f` - force overwrite the repro directory. Otherwise if the directory already exists the tool will fail.
* `--project <project name>` - useful if there are multiple projects using illink in the binlog. Specify the name of the project for which the repro should be created. 